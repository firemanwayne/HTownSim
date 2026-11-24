using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>Runs focused live Play Mode checks without adding test code to the player.</summary>
public static class VerifyHouston
{
    /// <summary>Checks departure state, camera limits, movement, suppression and reset.</summary>
    public static string Run()
    {
        if(!Application.isPlaying) { throw new InvalidOperationException("Enter Play Mode first."); }

        var results=new List<string>();

        var truck=(GameObject.Find("Engine07") ?? GameObject.Find("Engine08"));

        var response=truck.GetComponent("FireResponse");

        var type=response.GetType();

        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;

        void Call(string method,params object[] args) { type.GetMethod(method,flags).Invoke(response,args); }

        float Number(string field) => (float)type.GetField(field,flags).GetValue(response);

        void Check(bool condition,string label)
        {
            if(!condition) { throw new InvalidOperationException(label); }

            results.Add("PASS: "+label);
        }

        Check(truck.GetComponent<AudioSource>().mute,"Startup siren is muted");

        foreach(var light in truck.GetComponentsInChildren<Light>())
        {
            Check(light.intensity==0f,"Startup emergency light is off");
        }

        // The driven apparatus decides the bay (ambulance x 20.3, ladder 12.3) and whether it carries water.
        var drivenProfile=type.GetProperty("Profile",flags).GetValue(response);

        var canWater=(bool)drivenProfile.GetType().GetProperty("CanSprayWater",flags).GetValue(drivenProfile);

        var bayX=canWater?12.3f:20.3f;

        Check(Vector3.Distance((Vector3)type.GetField("_spawn",flags).GetValue(response),new Vector3(bayX,0.2f,-20.8f))<0.01f,"Saved departure pose is the driven truck's bay ("+(canWater?"Ladder 7, the middle bay":"Ambulance 7, the street-left bay")+")");

        // The incident text names the unit being driven (its ApparatusProfile unit name).
        var profileValues=type.GetProperty("Profile",flags).GetValue(response);

        var unit=((string)profileValues.GetType().GetProperty("UnitName",flags).GetValue(profileValues)).ToUpperInvariant();

        var statusText=((TMPro.TMP_Text)type.GetField("_status",flags).GetValue(response)).text;

        Check(statusText.ToUpperInvariant().Contains(unit),"Incident text names the unit dispatched ("+unit+")");

        // The dispatch alert follows the unit: an EMS unit hears the high/low tone with no station ring, a fire unit the ring and beeps.
        var emsAlert=(bool)profileValues.GetType().GetProperty("EmsAlert",flags).GetValue(profileValues);

        Call("PlayDispatch");

        var alertSource=(AudioSource)type.GetField("_alertSource",flags).GetValue(response);

        Check(emsAlert==(alertSource.clip!=null && alertSource.clip.name=="EmsDispatchHighLow"),"Dispatch alert matches the unit ("+(emsAlert?"EMS high/low tone":"fire ring and beeps")+")");

        // The key list is off by default and lists only what this vehicle has (no water or aerial rows for the ambulance).
        var controlsText=(TMPro.TMP_Text)type.GetField("_controlsText",flags).GetValue(response);

        Check(controlsText!=null && !controlsText.enabled,"Controls list is hidden until its key is held");

        Check(controlsText!=null && controlsText.text.Contains("TAB") && (canWater || (!controlsText.text.Contains("WATER") && !controlsText.text.Contains("LADDER"))),"Controls list is specific to the vehicle");

        Call("Restart");

        var spawn=truck.transform.position;

        Check(Vector3.Distance(spawn,new Vector3(bayX,0.2f,-20.8f))<0.3f,"Truck starts in its bay");

        var follow=UnityEngine.Object.FindAnyObjectByType<CinemachineFollow>();

        var offset=follow.FollowOffset;

        Call("OrbitCamera",new Vector2(240,0));

        Check(Vector3.Distance(offset,follow.FollowOffset)>1,"Horizontal mouse delta orbits camera");

        Check(Mathf.Abs(offset.magnitude-follow.FollowOffset.magnitude)<0.01f,"Orbit preserves camera distance");

        Call("OrbitCamera",new Vector2(0,-10000));

        Check(Number("_orbitPitch")==65f,"Upper orbit limit holds");

        Call("OrbitCamera",new Vector2(0,10000));

        Check(Number("_orbitPitch")==12f,"Lower orbit limit holds");

        Call("Restart");

        for(var i=0;i<60;i++) { Call("Drive",new Vector2(0,1),false); }

        Check(Vector3.Distance(spawn,truck.transform.position)>0.1f,"Driving advances truck from station");

        Call("Restart");

        Call("Suppress",1f);

        Check(Number("_heat")==1f,"Water cannot suppress fire from station");

        var controller=truck.GetComponent<CharacterController>();

        controller.enabled=false;

        var incident=GameObject.Find("FireIncident").transform.position;

        if(canWater)
        {
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                controller.enabled=false;

                truck.transform.position=new Vector3(incident.x,0.2f,incident.z)+direction*12f;

                controller.enabled=true;

                Physics.SyncTransforms();

                Call("Suppress",1f);

                if(Number("_heat")<1f) { break; }
            }
            Check(Number("_heat")<1f,"In-range water reduces fire");

            Call("Suppress",20f);

            Check(Number("_heat")==0f,"Sustained water contains fire");
        }
        else
        {
            // The ambulance carries no water: Suppress does nothing even right beside the fire. Then end the fire by hand to carry on.
            controller.enabled=false;

            truck.transform.position=new Vector3(incident.x,0.2f,incident.z)+Vector3.right*12f;

            controller.enabled=true;

            Physics.SyncTransforms();

            Call("Suppress",20f);

            Check(Number("_heat")==1f && !((LineRenderer)type.GetField("_water",flags).GetValue(response)).enabled,"Ambulance cannot spray water");

            type.GetField("_heat",flags).SetValue(response,0f);
        }

        bool CanDrive() => (bool)type.GetProperty("CanDrive",flags).GetValue(response);

        Check(CanDrive(),"Engine stays drivable after the fire is out (free roam)");

        Check(((Transform[])type.GetField("_incidentSites",flags).GetValue(response)).Length>=5,"Several storefront incident sites are configured");

        var beforeCall=GameObject.Find("FireIncident").transform.position;

        Call("AdvanceRoam",100f);

        Check(Number("_heat")==1f && Vector3.Distance(beforeCall,GameObject.Find("FireIncident").transform.position)>50f,"Free roam starts a new call at a different storefront");

        type.GetField("_sirenEnabled",flags).SetValue(response,true);

        type.GetField("_lightsEnabled",flags).SetValue(response,true);

        Call("Restart");

        Check(Number("_heat")==1f && Number("_remaining")==180f,"Restart restores incident and time");

        Check(Vector3.Distance(spawn,truck.transform.position)<0.3f,"Restart returns to the bay");

        Check(truck.GetComponent<AudioSource>().mute && !(bool)type.GetField("_lightsEnabled",flags).GetValue(response),"Restart silences siren and lights");

        Check(Vector3.Distance(offset,follow.FollowOffset)<0.01f,"Restart restores camera framing");

        type.GetField("_remaining",flags).SetValue(response,0f);

        Call("Suppress",20f);

        Check(Number("_heat")==1f,"Expired response cannot extinguish fire");

        var flasher=truck.GetComponent("ApparatusLights");

        Check(flasher!=null,"Engine has flashing warning-light lenses");

        // The engine's own lens flasher only applies while the engine model is the one being driven.
        if(flasher!=null && System.Array.Exists((Renderer[])flasher.GetType().GetField("_lenses",flags).GetValue(flasher),r=>r.gameObject.activeInHierarchy))
        {
            var lenses=(Renderer[])flasher.GetType().GetField("_lenses",flags).GetValue(flasher);

            int Lit() { var n=0; foreach(var lens in lenses) { if(lens.sharedMaterial.name.EndsWith("On")) { n++; } } return n; }

            type.GetField("_lightsEnabled",flags).SetValue(response,false);

            flasher.GetType().GetMethod("Update",flags).Invoke(flasher,null);

            Check(Lit()==0,"Warning lenses are dull while the lights are off");

            // Each flashing lens follows its own flasher: lit while Repeat(phase[flasher] - delay, 1) < DutyCycle (see
            // ApparatusLights.Update). Steady lenses follow the headlights and turn-signal lenses the indicators, so they
            // are left out. The lights are always switched off again, even if a check fails, so a rerun starts clean.
            var lightsType=flasher.GetType();

            var flasherOf=(int[])lightsType.GetField("_flasher",flags).GetValue(flasher);

            var delay=(float[])lightsType.GetField("_delay",flags).GetValue(flasher);

            var steady=(bool[])lightsType.GetField("_steady",flags).GetValue(flasher);

            var signalSide=(int[])lightsType.GetField("_signalSide",flags).GetValue(flasher);

            var duty=(float)lightsType.GetField("DutyCycle",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();

            bool Flashing(int i) { return !steady[i] && signalSide[i]==0; }

            bool IsOn(int i) { return lenses[i].sharedMaterial.name.EndsWith("On"); }

            try
            {
                type.GetField("_lightsEnabled",flags).SetValue(response,true);

                lightsType.GetMethod("Update",flags).Invoke(flasher,null);

                var phase=(float[])lightsType.GetField("_phase",flags).GetValue(flasher);

                var flashing=0;

                var mismatched=0;

                for(var i=0;i<lenses.Length;i++)
                {
                    if(!Flashing(i)) { continue; }

                    flashing++;

                    if(IsOn(i)!=(Mathf.Repeat(phase[flasherOf[i]]-delay[i],1f)<duty)) { mismatched++; }
                }

                Check(flashing>0 && mismatched==0,"Flashing lenses follow their flash phase ("+flashing+" lenses, "+mismatched+" wrong)");

                // Put every flasher just inside the lit window of its first lens, then every such lens must light.
                var firstLens=new Dictionary<int,int>();

                for(var i=0;i<lenses.Length;i++) { if(Flashing(i) && !firstLens.ContainsKey(flasherOf[i])) { firstLens[flasherOf[i]]=i; } }

                foreach(var pair in firstLens) { phase[pair.Key]=Mathf.Repeat(delay[pair.Value]+duty*0.25f,1f); }

                lightsType.GetMethod("Update",flags).Invoke(flasher,null);

                var lit=0;

                foreach(var pair in firstLens) { if(IsOn(pair.Value)) { lit++; } }

                Check(firstLens.Count>0 && lit==firstLens.Count,"Warning lenses light when the lights are switched on ("+lit+" of "+firstLens.Count+" flashers lit)");
            }
            finally
            {
                type.GetField("_lightsEnabled",flags).SetValue(response,false);

                lightsType.GetMethod("Update",flags).Invoke(flasher,null);
            }
        }

        // Scene lights: F switches the molded front floodlights; their lens faces glow and two spot lights come on.
        var scene=truck.GetComponent("SceneLights");

        Check(scene!=null,"Engine has switchable scene lights");

        if(scene!=null)
        {
            var sceneType=scene.GetType();

            bool SceneLit() { return (bool)sceneType.GetProperty("Lit",flags).GetValue(scene); }

            void ToggleScene() { type.GetMethod("ToggleSceneLights",flags).Invoke(response,null); sceneType.GetMethod("LateUpdate",flags).Invoke(scene,null); }

            var spots=new List<Light>();

            foreach(var light in truck.GetComponentsInChildren<Light>(true)) { if(light.name.StartsWith("SceneLight")) { spots.Add(light); } }

            var lensFaces=new List<Renderer>();

            foreach(var renderer in truck.GetComponentsInChildren<Renderer>(true)) { if(renderer.name=="Engine07_SceneLenses") { lensFaces.Add(renderer); } }

            Check(!SceneLit() && spots.Count==2 && spots.TrueForAll(s=>!s.enabled),"Scene lights start off");

            try
            {
                ToggleScene();

                Check(SceneLit() && spots.TrueForAll(s=>s.enabled) && lensFaces.Count>0 && lensFaces.TrueForAll(r=>r.sharedMaterial.name=="SceneLensOn"),"F turns the scene lights and their lenses on ("+lensFaces.Count+" lens object, "+spots.Count+" spots)");

                ToggleScene();

                Check(!SceneLit() && spots.TrueForAll(s=>!s.enabled) && lensFaces.TrueForAll(r=>r.sharedMaterial.name!="SceneLensOn"),"F turns the scene lights off again");
            }
            finally
            {
                if(SceneLit()) { ToggleScene(); }
            }
        }

        // Air horn: synthesized start + seamless loop; blows while held, fades on release, restart silences it.
        var airHorn=truck.GetComponent("AirHorn");

        // The profile decides: engine and ladder have the air horn, the ambulance (AirHorn off) the plain electric horn.
        var profileNow=type.GetProperty("Profile",flags).GetValue(response);

        var profileAirHorn=(bool)profileNow.GetType().GetProperty("AirHorn",flags).GetValue(profileNow);

        var engineDefault=type.Assembly.GetType("HTownSim.Runtime.ApparatusProfile").GetField("Engine",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).GetValue(null);

        Check((bool)engineDefault.GetType().GetProperty("AirHorn",flags).GetValue(engineDefault),"Engine and ladder profiles use the air horn");

        // The ambulance keeps the original Horn.mp3, the electric horn (it has no air horn).
        Check(Resources.Load<AudioClip>("SirenAudio/Horn")!=null,"Original electric horn clip (Horn.mp3) is present");

        Check((airHorn!=null)==profileAirHorn,profileAirHorn?"Air horn is fitted":"Ambulance has no air horn (plain electric horn)");

        if(airHorn!=null)
        {
            var hornType=airHorn.GetType();

            var hornSources=airHorn.GetComponents<AudioSource>();

            var startClip=Array.Find(hornSources,s=>s.clip!=null && s.clip.name=="AirHornStart");

            var loopClip=Array.Find(hornSources,s=>s.clip!=null && s.clip.name=="AirHornLoop");

            Check(startClip!=null && loopClip!=null && loopClip.loop && !startClip.loop,"Air horn has its start clip and looping hold clip");

            hornType.GetMethod("Press",flags).Invoke(airHorn,null);

            Check((bool)hornType.GetProperty("Held",flags).GetValue(airHorn) && (bool)hornType.GetProperty("Sounding",flags).GetValue(airHorn) && (bool)type.GetProperty("HornSounding",flags).GetValue(response),"Air horn blows when pressed");

            hornType.GetMethod("Release",flags).Invoke(airHorn,null);

            Check(!(bool)hornType.GetProperty("Held",flags).GetValue(airHorn) && (bool)hornType.GetProperty("Sounding",flags).GetValue(airHorn),"Air horn fades out rather than cutting off when released");

            // What Restart does for the horn (Restart itself would reset state that later checks rely on).
            hornType.GetMethod("StopNow",flags).Invoke(airHorn,null);

            Check(!(bool)hornType.GetProperty("Sounding",flags).GetValue(airHorn),"Air horn can be silenced at once (restart)");
        }

        // Aerial (Ladder 07): separate controls for outriggers out/in, outriggers down/up, ladder raise/lower, ladder
        // extend/retract and rotate. The ladder will not raise until the outriggers are at least 75% out and down.
        var aerial=truck.GetComponent("AerialController");

        if(aerial!=null)
        {
            var aerialType=aerial.GetType();

            var inputType=aerialType.Assembly.GetType("HTownSim.Runtime.AerialInput");

            float Value(string name) { return (float)aerialType.GetProperty(name,flags).GetValue(aerial); }

            bool Flag(string name) { return (bool)aerialType.GetProperty(name,flags).GetValue(aerial); }

            // Hold the given controls (outriggers out, down, raise, extend, rotate; each -1..1) for some seconds.
            void Hold(float seconds,float outrigger,float down,float raise,float extend,float slew)
            {
                var input=Activator.CreateInstance(inputType,flags,null,new object[]{outrigger,down,raise,extend,slew},null);

                for(var t=0f;t<seconds;t+=0.1f) { aerialType.GetMethod("Operate",flags).Invoke(aerial,new object[]{input,0.1f}); }
            }

            Transform Part(string name) { foreach(var t in truck.GetComponentsInChildren<Transform>(true)) { if(t.name==name) { return t; } } return null; }

            var pad=Part("CTRL_Outrigger_4.56_1");

            Vector3 PadLocal() { return truck.transform.InverseTransformPoint(pad.position); }

            var pad0=PadLocal();

            var tip0=((Vector3)aerialType.GetProperty("TipWorld",flags).GetValue(aerial)).y;

            Check(Flag("Stowed"),"Aerial starts stowed");

            try
            {
                Hold(2f,0f,0f,1f,0f,0f);

                Check(Value("Elevation")==0f && ((string)aerialType.GetProperty("Notice",flags).GetValue(aerial)).Contains("OUTRIGGERS"),"Aerial: ladder will not raise with the outriggers in");

                // Outrigger warning beep: loops while an outrigger moves, stops when the control stops.
                Hold(0.5f,1f,0f,0f,0f,0f);

                Check(Flag("BeepSounding") && Flag("Working") && (bool)type.GetProperty("AerialHydraulicsWorking",flags).GetValue(response),"Aerial: outrigger beep sounds and the engine revs while the outriggers move");

                Hold(0.1f,0f,0f,0f,0f,0f);

                Check(!Flag("BeepSounding") && !Flag("Working"),"Aerial: beep stops and the engine settles when the control stops");

                var beepSources=new List<AudioSource>((IEnumerable<AudioSource>)aerialType.GetProperty("BeepSources",flags).GetValue(aerial));

                Check(beepSources.Count==4 && beepSources.TrueForAll(s=>s.transform.parent.name.StartsWith("CTRL_Outrigger_") && s.spatialBlend>=1f && s.clip!=null && s.clip.length<0.2f && s.GetComponent<AudioLowPassFilter>()!=null && s.GetComponent<AudioLowPassFilter>().cutoffFrequency<5000f),"Aerial: muffled beep comes from each of the 4 outrigger pads (clip "+(beepSources.Count>0 && beepSources[0].clip!=null?beepSources[0].clip.length.ToString("0.00"):"-")+" s loop)");

                Hold(3.5f,1f,0f,0f,0f,0f);

                var padOut=Mathf.Abs(PadLocal().x)-Mathf.Abs(pad0.x);

                Check(Value("OutriggerOut")>=1f && Value("OutriggerDown")==0f && Mathf.Abs(padOut-1.15f)<0.05f && Mathf.Abs(PadLocal().y-pad0.y)<0.01f,"Aerial: outriggers extend on their own control ("+padOut.ToString("0.00")+" m out, not down)");

                Hold(2f,0f,0f,1f,0f,0f);

                Check(Value("Elevation")==0f,"Aerial: ladder will not raise with the outriggers out but not down");

                Hold(1.3f,0f,1f,0f,0f,0f);

                Hold(2f,0f,0f,1f,0f,0f);

                Check(Value("OutriggerDown")<0.75f && Value("Elevation")==0f,"Aerial: ladder will not raise with the outriggers "+(Value("OutriggerDown")*100f).ToString("0")+"% down (needs 75%)");

                Hold(0.4f,0f,1f,0f,0f,0f);

                var drop=pad0.y-PadLocal().y;

                Hold(8f,0f,0f,1f,0f,0f);

                var tip=((Vector3)aerialType.GetProperty("TipWorld",flags).GetValue(aerial)).y;

                Check(Value("OutriggerDown")>=0.75f && Value("Elevation")>50f && tip>tip0+5f && drop>0.15f,"Aerial: ladder raises once the outriggers are 75% out and down (pads down "+drop.ToString("0.00")+" m, tip up "+(tip-tip0).ToString("0.0")+" m)");

                Hold(2f,-1f,-1f,0f,0f,0f);

                Check(Value("OutriggerOut")>=1f && Value("OutriggerDown")>=0.75f,"Aerial: outriggers stay set while the ladder is up");

                Hold(2f,0f,0f,0f,1f,1f);

                Check(Value("Extension")>0.2f && Value("Azimuth")>30f && Flag("Working") && !Flag("BeepSounding"),"Aerial: ladder extends and rotates on its own controls (engine revs, no outrigger beep)");

                Check((bool)type.GetProperty("AerialDeployed",flags).GetValue(response),"Aerial: truck held still while set up");
            }
            finally
            {
                aerialType.GetMethod("ResetStowed",flags).Invoke(aerial,null);
            }

            Check(Flag("Stowed") && (PadLocal()-pad0).magnitude<0.01f,"Aerial: restart stows it");

            // Aerial panel: replaces the siren panel only while the outriggers are set; lamp shows bed alignment.
            var hud=GameObject.Find("AerialHudDriver")?.GetComponent("AerialHud");

            Check(hud!=null,"Aerial panel exists");

            if(hud!=null)
            {
                var hudType=hud.GetType();

                void Refresh() { hudType.GetMethod("Update",flags).Invoke(hud,null); }

                bool Showing() { return (bool)hudType.GetProperty("Showing",flags).GetValue(hud); }

                bool SirenShowing() { var siren=(Component)hudType.GetField("_sirenHud",flags).GetValue(hud); var panel=(RectTransform)siren.GetType().GetProperty("Panel",flags).GetValue(siren); return panel.gameObject.activeSelf; }

                bool InLine() { return (bool)hudType.GetProperty("InLine",flags).GetValue(hud); }

                string Lamp() { return (string)hudType.GetProperty("LampLabel",flags).GetValue(hud); }

                try
                {
                    Refresh();

                    Check(!Showing() && SirenShowing(),"Aerial panel hidden in drive mode (siren panel shown)");

                    Hold(2f,1f,1f,0f,0f,0f);

                    Refresh();

                    Check(!Showing(),"Aerial panel hidden until the outriggers are set ("+(Value("OutriggerOut")*100f).ToString("0")+"% out)");

                    Hold(2f,1f,1f,0f,0f,0f);

                    Refresh();

                    Check(Showing() && !SirenShowing() && InLine(),"Aerial panel replaces the siren panel once the outriggers are set; ladder in line with bed");

                    Hold(3f,0f,0f,1f,0f,0f);

                    Hold(1f,0f,0f,0f,0f,1f);

                    Refresh();

                    Check(!InLine() && Lamp().Contains("ROTATE LEFT"),"Aerial panel: rotated ladder shows the way back to its bed ("+Lamp()+")");

                    Hold(1f,0f,0f,0f,0f,-1f);

                    Refresh();

                    Check(InLine() && Lamp().Contains("IN LINE"),"Aerial panel: back in line with its bed ("+Lamp()+")");
                }
                finally
                {
                    aerialType.GetMethod("ResetStowed",flags).Invoke(aerial,null);

                    Refresh();
                }

                Check(!Showing() && SirenShowing(),"Aerial panel gives the spot back to the siren panel when stowed");
            }

            // Water keeps flowing from the tip with the ladder raised high and fully extended (tip 30+ m from the fire).
            var firePosition=GameObject.Find("FireIncident").transform.position;

            var waterLine=(LineRenderer)type.GetField("_water",flags).GetValue(response);

            // Later checks depend on the current response state, so it is put back exactly afterwards.
            var savedHeat=Number("_heat");

            var savedRemaining=Number("_remaining");

            var savedPosition=truck.transform.position;

            var savedRotation=truck.transform.rotation;

            try
            {
                // The fire site changes between runs, so try each side of it (as the ground water check does) until the
                // fully raised and extended ladder has a clear line from its tip.
                var flowed=false;

                var tipDistance=0f;

                foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
                {
                    aerialType.GetMethod("ResetStowed",flags).Invoke(aerial,null);

                    controller.enabled=false;

                    truck.transform.position=new Vector3(firePosition.x,0.2f,firePosition.z)+direction*12f;

                    truck.transform.rotation=Quaternion.LookRotation(-direction);

                    controller.enabled=true;

                    Physics.SyncTransforms();

                    type.GetField("_heat",flags).SetValue(response,1f);

                    type.GetField("_remaining",flags).SetValue(response,100f);

                    Hold(4f,1f,1f,0f,0f,0f);

                    Hold(8f,0f,0f,1f,0f,0f);

                    Hold(8.4f,0f,0f,0f,1f,0f);

                    tipDistance=Vector3.Distance((Vector3)aerialType.GetProperty("TipWorld",flags).GetValue(aerial),firePosition);

                    waterLine.enabled=false;

                    Call("Suppress",0.5f);

                    if(waterLine.enabled && Number("_heat")<1f) { flowed=true; break; }
                }

                Check(flowed && tipDistance>23f,"Aerial: water flows from the fully extended ladder (tip "+tipDistance.ToString("0")+" m from the fire)");
            }
            finally
            {
                aerialType.GetMethod("ResetStowed",flags).Invoke(aerial,null);

                waterLine.enabled=false;

                type.GetField("_heat",flags).SetValue(response,savedHeat);

                type.GetField("_remaining",flags).SetValue(response,savedRemaining);

                controller.enabled=false;

                truck.transform.SetPositionAndRotation(savedPosition,savedRotation);

                controller.enabled=true;

                Physics.SyncTransforms();
            }
        }

        var trafficObject=GameObject.Find("Traffic");

        var traffic=trafficObject!=null?trafficObject.GetComponent("TrafficSystem"):null;

        Check(traffic!=null,"Traffic system is present");

        if(traffic!=null)
        {
            var trafficType=traffic.GetType();

            int Count(string property) => (int)trafficType.GetProperty(property,flags).GetValue(traffic);

            Check(Count("ActiveCars")>=10,"Cars are driving around the player ("+Count("ActiveCars")+" active)");

            Check(Count("ActivePedestrians")>=10,"Pedestrians are walking the sidewalks ("+Count("ActivePedestrians")+" active)");
        }

        Check(!CanDrive(),"Expired response ends driving");

        var wall=GameObject.Find("WorldBounds");

        Check(wall!=null && wall.GetComponentsInChildren<BoxCollider>().Length==4,"Four boundary walls surround the city");

        var roamController=truck.GetComponent<CharacterController>();

        roamController.enabled=false;

        var eastEdge=((Vector2)type.GetField("_boundsMax",flags).GetValue(response)).x;

        truck.transform.position=new Vector3(eastEdge-20f,0.2f,0f);

        roamController.enabled=true;

        Physics.SyncTransforms();

        roamController.Move(new Vector3(80f,0f,0f));

        Check(truck.transform.position.x<=eastEdge+1f && truck.transform.position.x>eastEdge-8f,"Boundary wall stops the engine at the east edge ("+eastEdge+")");

        Call("Restart");

        return string.Join("\n",results);
    }
}
