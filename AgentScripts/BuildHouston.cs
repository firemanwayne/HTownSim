using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Creates the isolated Houston response scene using live Editor APIs.</summary>
public static class BuildHouston
{
    /// <summary>Output directory reserved for generated demo assets.</summary>
    private const string Root = "Assets/HTownSim/Generated";
    /// <summary>Materials shared by the block.</summary>
    private static Material _asphalt, _concrete, _brick, _cream, _red, _glass, _steel, _dark, _leaf, _amber;

    /// <summary>Builds once; refuses to overwrite an existing authored scene.</summary>
    public static string Build()
    {
        const string scenePath = "Assets/Scenes/HoustonResponse.unity";

        if (File.Exists(scenePath))
        {
            throw new InvalidOperationException("HoustonResponse already exists; edit it in place.");
        }

        Directory.CreateDirectory(Root);

        AssetDatabase.Refresh();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);

        _asphalt = Material("Asphalt", "303D43");

        _concrete = Material("Concrete", "AAA697");

        _brick = Material("Brick", "875946");

        _cream = Material("Limestone", "E6D7B7");

        _red = Material("ApparatusRed", "B52E32");

        _glass = Material("Glazing", "284B59");

        _steel = Material("Steel", "71858C");

        _dark = Material("Rubber", "18282F");

        _leaf = Material("LiveOak", "536D50");

        _amber = Material("Amber", "FFAF52");

        _amber.EnableKeyword("_EMISSION");

        _amber.SetColor("_EmissionColor", _amber.color * 2f);

        Box("Ground", (new Vector3(0,-0.3f,15), new Vector3(150,0.5f,190), _leaf));

        Box("CanalStreet", (new Vector3(0,0,10), new Vector3(22,0.12f,170), _asphalt));

        Box("CrossStreet", (new Vector3(0,0.01f,45), new Vector3(140,0.12f,20), _asphalt));

        foreach (var side in new[] {-1,1})
        {
            Box("Sidewalk", (new Vector3(side*14f,0.16f,0), new Vector3(6,0.3f,70), _concrete));

            Box("SidewalkNorth", (new Vector3(side*14f,0.16f,80), new Vector3(6,0.3f,50), _concrete));

            for (var z = -65; z < 95; z += 8)
            {
                if (z > 31 && z < 58) { continue; }

                Box("CenterLine", (new Vector3(side*0.18f,0.075f,z),new Vector3(0.12f,0.015f,4),_amber));
            }

            for (var z = -25; z <= 80; z += 35)
            {
                if (z == 45) { continue; }

                Tree(new Vector3(side*14,0,z));

                Box("LampPost", (new Vector3(side*11.5f,3.5f,z+7),new Vector3(0.15f,7,0.15f),_dark));

                Box("LampArm", (new Vector3(side*10.5f,7,z+7),new Vector3(2,0.12f,0.12f),_dark));

                Box("Lamp", (new Vector3(side*9.7f,6.9f,z+7),new Vector3(0.9f,0.12f,0.45f),_amber));
            }
        }

        for (var x=-9; x<=9; x+=3)
        {
            Box("Crosswalk", (new Vector3(x,0.09f,31), new Vector3(1.3f,0.02f,3), _cream));
        }

        Building("CANAL MARKET", (new Vector3(25,0,18), new Vector3(18,10,23), _brick));

        Building("BAYOU COFFEE", (new Vector3(-26,0,10),new Vector3(19,7,24),_cream));

        Building("EAST END / WORKSHOP", (new Vector3(27,0,-17),new Vector3(21,8,27),_concrete));

        Building("WAREHOUSE 08", (new Vector3(-29,0,-24),new Vector3(24,11,24),_brick));

        Building("CANAL LOFTS", (new Vector3(27,0,77),new Vector3(22,19,33),_cream));

        Building("EAST END SUPPLY", (new Vector3(-27,0,76),new Vector3(22,9,30),_brick));

        for (var i=0; i<13; i++)
        {
            var height=18+(i*17%35);

            Box("DistantSkyline",(new Vector3(-65+i*10,height/2f,135+(i%3)*8),new Vector3(8,height,9),i%2==0?_steel:_glass));
        }

        var incident = new GameObject("FireIncident").transform;

        incident.position = new Vector3(15.7f,3,18);

        var fire = Particles("Flames", (incident.position, ColorFrom("FFAF52"), 65f));

        var smoke = Particles("Smoke", (incident.position+Vector3.up*3, ColorFrom("303D43"), 15f));

        var smokeMain=smoke.main;

        smokeMain.startLifetime=7f;

        smokeMain.startSize=new ParticleSystem.MinMaxCurve(2f,4f);

        smokeMain.startSpeed=2.5f;

        var truck = Truck();

        var cameraObject = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener), typeof(CinemachineBrain));

        cameraObject.tag = "MainCamera";

        var camera = cameraObject.GetComponent<Camera>();

        camera.farClipPlane=350f;

        camera.backgroundColor=ColorFrom("71858C");

        camera.clearFlags=CameraClearFlags.SolidColor;

        camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;

        var rig = new GameObject("VehicleCamera").AddComponent<CinemachineCamera>();

        rig.Follow=truck.transform;

        rig.LookAt=truck.transform;

        rig.Lens.FieldOfView=58f;

        var follow=rig.gameObject.AddComponent<CinemachineFollow>();

        follow.FollowOffset=new Vector3(0,8,-15);

        rig.gameObject.AddComponent<CinemachineRotationComposer>();

        Lighting();

        var water=new GameObject("WaterMonitor").AddComponent<LineRenderer>();

        water.sharedMaterial=Material("Water", "B7DDE4");

        water.startWidth=0.16f;

        water.endWidth=0.4f;

        water.positionCount=20;

        water.enabled=false;

        var canvasObject = new GameObject("ResponseHud",typeof(Canvas),typeof(CanvasScaler));

        var canvas=canvasObject.GetComponent<Canvas>();

        canvas.renderMode=RenderMode.ScreenSpaceCamera;

        canvas.worldCamera=camera;

        canvas.planeDistance=1;

        var scaler=canvasObject.GetComponent<CanvasScaler>();

        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution=new Vector2(1600,900);

        scaler.matchWidthOrHeight=1f;

        var panel = new GameObject("DispatchPanel",typeof(RectTransform),typeof(UnityEngine.UI.Image));

        panel.transform.SetParent(canvas.transform,false);

        var panelRect=(RectTransform)panel.transform;

        panelRect.anchorMin=panelRect.anchorMax=panelRect.pivot=new Vector2(0,1);

        panelRect.anchoredPosition=new Vector2(28,-28);

        panelRect.sizeDelta=new Vector2(620,192);

        panel.GetComponent<UnityEngine.UI.Image>().color=ColorFrom("18282F");

        Text(canvas.transform,("H T O W N   /   ENGINE 08",new Rect(50,42,570,32),22,ColorFrom("FFAF52")));

        var status=Text(canvas.transform,("DISPATCH",new Rect(50,84,570,110),27,ColorFrom("E6D7B7")));

        var telemetry=Text(canvas.transform,("",new Rect(50,235,570,85),23,ColorFrom("E6D7B7")));

        var controls=Text(canvas.transform,("WASD  DRIVE     SPACE  BRAKE     E  WATER\nQ  SIREN     ESC  PAUSE     R  RESTART",new Rect(0,0,1100,76),22,ColorFrom("E6D7B7")));

        controls.rectTransform.anchorMin=controls.rectTransform.anchorMax=controls.rectTransform.pivot=new Vector2(0,0);

        controls.rectTransform.anchoredPosition=new Vector2(40,24);

        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("HTownSim.Runtime.FireResponse")).First(t=>t!=null);

        var response=truck.AddComponent(type);

        var serialized=new SerializedObject(response);

        serialized.FindProperty("_incident").objectReferenceValue=incident;

        serialized.FindProperty("_fire").objectReferenceValue=fire;

        serialized.FindProperty("_smoke").objectReferenceValue=smoke;

        serialized.FindProperty("_water").objectReferenceValue=water;

        serialized.FindProperty("_status").objectReferenceValue=status;

        serialized.FindProperty("_telemetry").objectReferenceValue=telemetry;

        var lights=truck.GetComponentsInChildren<Light>();

        var beacons=serialized.FindProperty("_beacons");

        beacons.arraySize=lights.Length;

        for (var i=0;i<lights.Length;i++)
        {
            beacons.GetArrayElementAtIndex(i).objectReferenceValue=lights[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene,scenePath);

        AssetDatabase.SaveAssets();

        return scenePath;
    }

    /// <summary>Creates a shared URP material.</summary>
    private static Material Material(string name,string hex)
    {
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=ColorFrom(hex)};

        material.SetFloat("_Smoothness",0.28f);

        AssetDatabase.CreateAsset(material,Root+"/"+name+".mat");

        return material;
    }

    /// <summary>Parses palette colors.</summary>
    private static Color ColorFrom(string hex)
    {
        ColorUtility.TryParseHtmlString("#"+hex,out var color);

        return color;
    }

    /// <summary>Creates a collidable piece of block architecture.</summary>
    private static GameObject Box(string name,(Vector3 Position,Vector3 Scale,Material Material) spec)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);

        go.name=name;

        go.transform.position=spec.Position;

        go.transform.localScale=spec.Scale;

        go.GetComponent<Renderer>().sharedMaterial=spec.Material;

        return go;
    }

    /// <summary>Adds window rhythms and roof trim to a storefront.</summary>
    private static void Building(string name,(Vector3 Position,Vector3 Scale,Material Material) spec)
    {
        Box(name,(spec.Position+Vector3.up*spec.Scale.y/2,spec.Scale,spec.Material));

        Box("RoofCoping",(spec.Position+Vector3.up*(spec.Scale.y+0.15f),new Vector3(spec.Scale.x+0.4f,0.3f,spec.Scale.z+0.4f),_cream));

        var x=spec.Position.x-Mathf.Sign(spec.Position.x)*(spec.Scale.x/2+0.08f);

        for (var z=spec.Position.z-spec.Scale.z/2+3;z<spec.Position.z+spec.Scale.z/2-1;z+=4)
        {
            for (var y=2f;y<spec.Scale.y-1;y+=3.3f)
            {
                Box("WindowFrame",(new Vector3(x,y,z),new Vector3(0.12f,2.3f,2.7f),_cream));

                Box("Window",(new Vector3(x-Mathf.Sign(x)*0.09f,y,z),new Vector3(0.12f,1.9f,2.3f),_glass));
            }
        }

        Box("Canopy",(new Vector3(x-Mathf.Sign(x),4.2f,spec.Position.z),new Vector3(2.4f,0.22f,spec.Scale.z-2),_red));
    }

    /// <summary>Creates a stylized live oak with a layered crown.</summary>
    private static void Tree(Vector3 position)
    {
        Box("OakTrunk",(position+Vector3.up*2,new Vector3(0.5f,4,0.5f),_brick));

        for(var i=0;i<3;i++)
        {
            var crown=GameObject.CreatePrimitive(PrimitiveType.Sphere);

            crown.name="OakCanopy";

            crown.transform.position=position+new Vector3((i-1)*1.1f,4.5f+(i%2),0);

            crown.transform.localScale=new Vector3(4,2.8f,4);

            crown.GetComponent<Renderer>().sharedMaterial=_leaf;

            UnityEngine.Object.DestroyImmediate(crown.GetComponent<Collider>());
        }
    }

    /// <summary>Creates a recognizable apparatus with wheels, lockers, and roof ladder.</summary>
    private static GameObject Truck()
    {
        var truck=new GameObject("Engine08");

        void Part(string name,(Vector3 Position,Vector3 Scale,Material Material) spec)
        {
            var part=Box(name,spec);

            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());

            part.transform.SetParent(truck.transform,false);
        }

        Part("Chassis",(new Vector3(0,0.8f,0),new Vector3(2.7f,0.5f,7.8f),_dark));

        Part("Cab",(new Vector3(0,1.9f,2.1f),new Vector3(2.8f,2.1f,3.1f),_red));

        Part("CabRoof",(new Vector3(0,3f,2.1f),new Vector3(2.85f,0.25f,3.15f),_cream));

        Part("Windshield",(new Vector3(0,2.35f,3.67f),new Vector3(2.5f,0.85f,0.05f),_glass));

        Part("Grille",(new Vector3(0,1.4f,3.69f),new Vector3(1.6f,0.55f,0.08f),_steel));

        Part("Bumper",(new Vector3(0,0.9f,3.9f),new Vector3(2.9f,0.3f,0.3f),_steel));

        Part("EquipmentBody",(new Vector3(0,1.6f,-1.6f),new Vector3(2.7f,1.9f,4.4f),_red));

        foreach(var side in new[]{-1,1})
        {
            Part("ReflectiveStripe",(new Vector3(side*1.4f,1.5f,0),new Vector3(0.05f,0.22f,7),_cream));

            for(var z=-3;z<=0;z+=1)
            {
                Part("EquipmentLocker",(new Vector3(side*1.37f,2,z),new Vector3(0.05f,0.9f,0.85f),_steel));
            }

            foreach(var z in new[]{-2.6f,2.5f})
            {
                var wheel=GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                wheel.name="Wheel";

                UnityEngine.Object.DestroyImmediate(wheel.GetComponent<Collider>());

                wheel.transform.SetParent(truck.transform,false);

                wheel.transform.localPosition=new Vector3(side*1.4f,0.65f,z);

                wheel.transform.localRotation=Quaternion.Euler(0,0,90);

                wheel.transform.localScale=new Vector3(1.3f,0.22f,1.3f);

                wheel.GetComponent<Renderer>().sharedMaterial=_dark;
            }

            Part("LadderRail",(new Vector3(side*0.48f,2.8f,-1.4f),new Vector3(0.1f,0.12f,4.5f),_steel));

            var beacon=new GameObject("EmergencyBeacon").AddComponent<Light>();

            beacon.transform.SetParent(truck.transform,false);

            beacon.transform.localPosition=new Vector3(side,3.3f,2.2f);

            beacon.color=_red.color;

            beacon.range=14;

            Part("Lightbar",(new Vector3(side,3.25f,2.2f),new Vector3(0.6f,0.25f,0.4f),_amber));
        }

        for(var z=-3;z<=0;z++)
        {
            Part("LadderRung",(new Vector3(0,2.8f,z),new Vector3(1,0.1f,0.1f),_steel));
        }

        var controller=truck.AddComponent<CharacterController>();

        controller.height=3.5f;

        controller.radius=1.45f;

        controller.center=new Vector3(0,1.8f,0);

        controller.stepOffset=0.2f;

        truck.transform.position=new Vector3(5,0.2f,-36);

        return truck;
    }

    /// <summary>Creates a bounded looping incident effect.</summary>
    private static ParticleSystem Particles(string name,(Vector3 Position,Color Color,float Rate) spec)
    {
        var ps=new GameObject(name).AddComponent<ParticleSystem>();

        ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);

        ps.transform.position=spec.Position;

        ps.transform.rotation=Quaternion.Euler(-90,0,0);

        var main=ps.main;

        main.startColor=spec.Color;

        main.startLifetime=1.4f;

        main.startSize=new ParticleSystem.MinMaxCurve(0.7f,1.5f);

        main.startSpeed=2f;

        main.maxParticles=200;

        var emission=ps.emission;

        emission.rateOverTime=spec.Rate;

        var shape=ps.shape;

        shape.shapeType=ParticleSystemShapeType.Cone;

        shape.radius=1.6f;

        shape.angle=12;

        var size=ps.sizeOverLifetime;

        size.enabled=true;

        size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.Linear(0,1,1,0));

        var material=new Material(Shader.Find("Sprites/Default")){name=name+"Particles"};

        AssetDatabase.CreateAsset(material,Root+"/"+name+"Particles.mat");

        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;

        ps.Play();

        return ps;
    }

    /// <summary>Authors the dusk light and restrained post processing.</summary>
    private static void Lighting()
    {
        RenderSettings.ambientMode=AmbientMode.Trilight;

        RenderSettings.ambientSkyColor=ColorFrom("71858C");

        RenderSettings.ambientEquatorColor=ColorFrom("AAA697");

        RenderSettings.ambientGroundColor=ColorFrom("303D43");

        RenderSettings.fog=true;

        RenderSettings.fogColor=ColorFrom("71858C");

        RenderSettings.fogMode=FogMode.Linear;

        RenderSettings.fogStartDistance=80;

        RenderSettings.fogEndDistance=230;

        var sun=new GameObject("DuskSun").AddComponent<Light>();

        sun.type=LightType.Directional;

        sun.color=ColorFrom("FFE1B0");

        sun.intensity=1.2f;

        sun.shadows=LightShadows.Soft;

        sun.transform.rotation=Quaternion.Euler(38,-32,0);

        var volume=new GameObject("DuskGrade").AddComponent<Volume>();

        volume.isGlobal=true;

        var profile=ScriptableObject.CreateInstance<VolumeProfile>();

        profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);

        profile.Add<Bloom>().intensity.Override(0.3f);

        profile.Add<Vignette>().intensity.Override(0.2f);

        profile.Add<ColorAdjustments>().contrast.Override(8f);

        AssetDatabase.CreateAsset(profile,Root+"/DuskGrade.asset");

        foreach(var component in profile.components)
        {
            AssetDatabase.AddObjectToAsset(component,profile);
        }

        volume.sharedProfile=profile;
    }

    /// <summary>Creates a readable, top-left-anchored HUD label.</summary>
    private static TMP_Text Text(Transform parent,(string Content,Rect Rect,int Size,Color Color) spec)
    {
        var text=new GameObject("HudLabel",typeof(RectTransform)).AddComponent<TextMeshProUGUI>();

        text.transform.SetParent(parent,false);

        text.rectTransform.anchorMin=text.rectTransform.anchorMax=text.rectTransform.pivot=new Vector2(0,1);

        text.rectTransform.anchoredPosition=new Vector2(spec.Rect.x,-spec.Rect.y);

        text.rectTransform.sizeDelta=spec.Rect.size;

        text.text=spec.Content;

        text.fontSize=spec.Size;

        text.fontStyle=FontStyles.Bold;

        text.characterSpacing=1f;

        text.color=spec.Color;

        text.raycastTarget=false;

        return text;
    }
}
