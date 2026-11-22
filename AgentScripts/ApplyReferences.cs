using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Refines the demo from the supplied engine and Houston station photographs.</summary>
public static class ApplyReferences
{
    /// <summary>Applies a single reference-detail pass without replacing gameplay objects.</summary>
    public static string Apply()
    {
        var truck=GameObject.Find("Engine08").transform;

        if(truck.Find("ReferenceDetails") != null)
        {
            return "Reference details already applied.";
        }

        var detail=new GameObject("ReferenceDetails").transform;

        detail.SetParent(truck,false);

        Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/"+name+".mat");

        var red=Mat("ApparatusRed");

        var white=Mat("Limestone");

        var steel=Mat("Steel");

        var dark=Mat("Rubber");

        var glass=Mat("Glazing");

        steel.SetFloat("_Metallic",0.75f);

        steel.SetFloat("_Smoothness",0.65f);

        truck.Find("CabRoof").GetComponent<Renderer>().sharedMaterial=red;

        foreach(var lens in truck.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Lightbar"))
        {
            lens.sharedMaterial=red;
        }

        foreach(var light in truck.GetComponentsInChildren<Light>())
        {
            light.intensity=0f;
        }

        void Part(string name,(Vector3 Position,Vector3 Scale,Material Material) spec)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);

            go.name=name;

            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.transform.SetParent(detail,false);

            go.transform.localPosition=spec.Position;

            go.transform.localScale=spec.Scale;

            go.GetComponent<Renderer>().sharedMaterial=spec.Material;
        }

        Part("RaisedCrewRoof",(new Vector3(0,3.18f,1.35f),new Vector3(2.8f,0.4f,1.5f),red));

        Part("WindshieldDivider",(new Vector3(0,2.35f,3.71f),new Vector3(0.07f,0.87f,0.07f),red));

        Part("GrilleSurround",(new Vector3(0,1.55f,3.76f),new Vector3(1.65f,0.95f,0.08f),steel));

        for(var row=0;row<6;row++)
        {
            for(var column=0;column<6;column++)
            {
                Part("GrilleSlot",(new Vector3(-0.65f+column*0.26f,1.18f+row*0.145f,3.81f),new Vector3(0.18f,0.065f,0.015f),dark));
            }
        }

        foreach(var side in new[]{-1,1})
        {
            Part("DriverWindow",(new Vector3(side*1.415f,2.35f,2.8f),new Vector3(0.04f,0.92f,1.2f),glass));

            Part("CrewWindow",(new Vector3(side*1.415f,2.35f,1.25f),new Vector3(0.04f,0.85f,0.95f),glass));

            Part("DoorSeam",(new Vector3(side*1.43f,1.95f,2.03f),new Vector3(0.03f,1.8f,0.025f),dark));

            Part("DoorHandle",(new Vector3(side*1.46f,1.98f,2.25f),new Vector3(0.06f,0.1f,0.3f),steel));

            Part("MirrorArm",(new Vector3(side*1.7f,2.2f,3.2f),new Vector3(0.55f,0.07f,0.08f),steel));

            Part("Mirror",(new Vector3(side*1.95f,2.5f,3.2f),new Vector3(0.13f,0.55f,0.25f),steel));

            Part("CabStep",(new Vector3(side*1.48f,0.85f,1.25f),new Vector3(0.3f,0.12f,1.3f),steel));

            Part("HeadlampFrame",(new Vector3(side*1.1f,1.55f,3.75f),new Vector3(0.45f,0.65f,0.12f),steel));

            Part("HeadlampLens",(new Vector3(side*1.1f,1.65f,3.83f),new Vector3(0.33f,0.22f,0.04f),white));

            Part("PumpPanel",(new Vector3(side*1.4f,1.85f,-0.05f),new Vector3(0.08f,1.45f,0.9f),dark));

            for(var row=0;row<3;row++)
            {
                for(var col=0;col<3;col++)
                {
                    var gauge=GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                    gauge.name="PumpGauge";

                    Object.DestroyImmediate(gauge.GetComponent<Collider>());

                    gauge.transform.SetParent(detail,false);

                    gauge.transform.localPosition=new Vector3(side*1.46f,1.4f+row*0.35f,-0.35f+col*0.3f);

                    gauge.transform.localRotation=Quaternion.Euler(0,0,90);

                    gauge.transform.localScale=new Vector3(0.15f,0.025f,0.15f);

                    gauge.GetComponent<Renderer>().sharedMaterial=steel;
                }
            }

            for(var stripe=0;stripe<3;stripe++)
            {
                Part("BodyStripe",(new Vector3(side*1.42f,2.15f+stripe*0.13f,-2.1f),new Vector3(0.03f,0.055f,3.25f),white));
            }

            foreach(var z in new[]{-2.6f,2.5f})
            {
                var hub=GameObject.CreatePrimitive(PrimitiveType.Cylinder);

                hub.name="ChromeHub";

                Object.DestroyImmediate(hub.GetComponent<Collider>());

                hub.transform.SetParent(detail,false);

                hub.transform.localPosition=new Vector3(side*1.64f,0.65f,z);

                hub.transform.localRotation=Quaternion.Euler(0,0,90);

                hub.transform.localScale=new Vector3(0.72f,0.035f,0.72f);

                hub.GetComponent<Renderer>().sharedMaterial=steel;
            }
        }

        var station=GameObject.Find("Station08").transform;

        truck.position=new Vector3(9,0.2f,-48.5f);

        var apron=station.Find("StationApron");

        apron.position=new Vector3(4,0.08f,-55);

        apron.localScale=new Vector3(32,0.15f,20);

        void Resize(string name,(Vector3 Position,Vector3 Scale) spec)
        {
            var item=station.Find(name);

            item.position=spec.Position;

            item.localScale=spec.Scale;
        }

        Resize("RearWall",(new Vector3(1,5.5f,-78),new Vector3(24,11,0.5f)));

        Resize("RightWall",(new Vector3(13,5.5f,-71),new Vector3(0.6f,11,14)));

        Resize("StationOffice",(new Vector3(-11,5.5f,-71),new Vector3(0.6f,11,14)));

        Resize("Roof",(new Vector3(1,11.2f,-71),new Vector3(24.5f,0.3f,14.5f)));

        Resize("Fascia",(new Vector3(1,5.3f,-64),new Vector3(24,0.6f,0.7f)));

        station.Find("OfficeRoof").gameObject.SetActive(false);

        foreach(var child in station.GetComponentsInChildren<Transform>().Where(t=>t.name=="OfficeWindow"))
        {
            child.gameObject.SetActive(false);
        }

        void StationPart(string name,(Vector3 Position,Vector3 Scale,Material Material) spec)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);

            go.name=name;

            go.transform.SetParent(station,false);

            go.transform.position=spec.Position;

            go.transform.localScale=spec.Scale;

            go.GetComponent<Renderer>().sharedMaterial=spec.Material;
        }

        foreach(var x in new[]{-7f,1f,9f})
        {
            StationPart("UpperGlazing",(new Vector3(x,8.2f,-64),new Vector3(7.2f,5,0.16f),glass));

            foreach(var offset in new[]{-3.8f,3.8f})
            {
                StationPart("FacadePier",(new Vector3(x+offset,5.5f,-63.8f),new Vector3(0.4f,11,0.5f),white));
            }

            for(var offset=-2;offset<=2;offset+=2)
            {
                StationPart("WindowMullion",(new Vector3(x+offset,8.2f,-63.85f),new Vector3(0.065f,5,0.08f),white));
            }

            StationPart("WindowTransom",(new Vector3(x,8,-63.84f),new Vector3(7.2f,0.07f,0.08f),white));
        }

        StationPart("StationHeader",(new Vector3(1,11,-63.75f),new Vector3(24,0.6f,0.4f),white));

        var sign=station.Find("StationName").GetComponent<TMP_Text>();

        sign.transform.position=new Vector3(1,11,-63.5f);

        sign.color=dark.color;

        sign.text="H T O W N   FIRE STATION   /   08";

        sign.fontSize=6;

        foreach(var text in Object.FindObjectsByType<TMP_Text>())
        {
            if(text.text.StartsWith("WASD"))
            {
                text.text="WASD  DRIVE   SPACE  BRAKE   E  WATER   RMB + DRAG  LOOK\nQ  SIREN   L  LIGHTS   ESC  PAUSE   R  RETURN TO STATION";
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();

        return "Engine detailing, reference-inspired three-bay station and control hints saved.";
    }
}
