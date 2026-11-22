using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Applies the first visual review corrections to the existing scene.</summary>
public static class PolishHouston
{
    /// <summary>Improves framing, apparatus identification, and HUD contrast.</summary>
    public static string Apply()
    {
        var rig=GameObject.Find("VehicleCamera");

        rig.GetComponent<CinemachineFollow>().FollowOffset=new Vector3(0,6,-14);

        rig.GetComponent<CinemachineRotationComposer>().TargetOffset=new Vector3(0,2.7f,3);

        var panel=GameObject.Find("DispatchPanel").GetComponent<RectTransform>();

        panel.sizeDelta=new Vector2(620,295);

        var truck=GameObject.Find("Engine08").transform;

        var cream=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Limestone.mat");

        var red=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/ApparatusRed.mat");

        var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Steel.mat");

        for(var i=0;i<8;i++)
        {
            var stripe=GameObject.CreatePrimitive(PrimitiveType.Cube);

            stripe.name="RearChevron";

            Object.DestroyImmediate(stripe.GetComponent<Collider>());

            stripe.transform.SetParent(truck,false);

            stripe.transform.localPosition=new Vector3(-1.15f+i*0.33f,1.35f,-3.82f);

            stripe.transform.localRotation=Quaternion.Euler(0,0,i<4?-30:30);

            stripe.transform.localScale=new Vector3(0.18f,0.8f,0.025f);

            stripe.GetComponent<Renderer>().sharedMaterial=cream;
        }

        Label(truck,("ENGINE 08",new Vector3(0,2.2f,-3.83f),Quaternion.Euler(0,180,0),2.5f));

        var sign=new GameObject("CanalMarketSign").transform;

        sign.position=new Vector3(15.82f,5.2f,18);

        sign.rotation=Quaternion.Euler(0,-90,0);

        Label(sign,("CANAL MARKET",Vector3.zero,Quaternion.identity,4f));

        var smoke=GameObject.Find("Smoke").GetComponent<ParticleSystem>();

        smoke.transform.position=new Vector3(16.3f,9f,18);

        var main=smoke.main;

        main.startLifetime=9f;

        main.startSize=new ParticleSystem.MinMaxCurve(2.5f,4.5f);

        var profile=GameObject.Find("DuskGrade").GetComponent<UnityEngine.Rendering.Volume>().sharedProfile;

        foreach(var component in profile.components)
        {
            if(!AssetDatabase.Contains(component))
            {
                AssetDatabase.AddObjectToAsset(component,profile);
            }
        }

        EditorUtility.SetDirty(profile);

        AssetDatabase.SaveAssets();

        foreach(var z in new[]{-75f,110f})
        {
            var barrier=GameObject.CreatePrimitive(PrimitiveType.Cube);

            barrier.name="RoadClosure";

            barrier.transform.position=new Vector3(0,0.65f,z);

            barrier.transform.localScale=new Vector3(23,1.3f,0.7f);

            barrier.GetComponent<Renderer>().sharedMaterial=cream;
        }

        foreach(var x in new[]{-72f,72f})
        {
            var barrier=GameObject.CreatePrimitive(PrimitiveType.Cube);

            barrier.name="CrossStreetClosure";

            barrier.transform.position=new Vector3(x,0.65f,45);

            barrier.transform.localScale=new Vector3(0.7f,1.3f,21f);

            barrier.GetComponent<Renderer>().sharedMaterial=cream;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        return "Framing, HUD contrast, scene identifiers, smoke visibility and road closures updated.";
    }

    /// <summary>Adds deliberately styled world text.</summary>
    private static void Label(Transform parent,(string Text,Vector3 Position,Quaternion Rotation,float Width) spec)
    {
        var text=new GameObject("Identification").AddComponent<TextMeshPro>();

        text.transform.SetParent(parent,false);

        text.transform.localPosition=spec.Position;

        text.transform.localRotation=spec.Rotation;

        text.text=spec.Text;

        text.fontSize=spec.Width==4f?7f:4f;

        text.fontStyle=FontStyles.Bold;

        text.characterSpacing=2f;

        text.alignment=TextAlignmentOptions.Center;

        text.rectTransform.sizeDelta=new Vector2(spec.Width,0.7f);

        text.color=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Limestone.mat").color;
    }
}
