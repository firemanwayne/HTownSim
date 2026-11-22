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

        Check(Vector3.Distance((Vector3)type.GetField("_spawn",flags).GetValue(response),new Vector3(12.3f,0.2f,-20.8f))<0.01f,"Saved departure pose is Engine 7's bay");

        Call("Restart");

        var spawn=truck.transform.position;

        Check(Vector3.Distance(spawn,new Vector3(12.3f,0.2f,-20.8f))<0.3f,"Truck starts in its bay");

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

        if(flasher!=null)
        {
            var lenses=(Renderer[])flasher.GetType().GetField("_lenses",flags).GetValue(flasher);

            int Lit() { var n=0; foreach(var lens in lenses) { if(lens.sharedMaterial.name.EndsWith("On")) { n++; } } return n; }

            type.GetField("_lightsEnabled",flags).SetValue(response,false);

            flasher.GetType().GetMethod("Update",flags).Invoke(flasher,null);

            Check(Lit()==0,"Warning lenses are dull while the lights are off");

            type.GetField("_lightsEnabled",flags).SetValue(response,true);

            flasher.GetType().GetMethod("Update",flags).Invoke(flasher,null);

            var groups=(int[])flasher.GetType().GetField("_group",flags).GetValue(flasher);

            var expected=0;

            for(var i=0;i<groups.Length;i++) { if(Mathf.Repeat(Time.time*3f+groups[i]*0.5f,1f)<0.45f) { expected++; } }

            Check(expected==0 || Lit()==expected,"Warning lenses light when the lights are switched on ("+expected+" of "+groups.Length+" lit at this instant)");

            type.GetField("_lightsEnabled",flags).SetValue(response,false);
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
