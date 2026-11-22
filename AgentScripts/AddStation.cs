using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Adds the station departure point and repairs persistent grading settings.</summary>
public static class AddStation
{
    /// <summary>Creates the station on the south end of Canal Street.</summary>
    public static string Build()
    {
        if(GameObject.Find("Station08") != null)
        {
            return "Station already exists.";
        }

        var root=new GameObject("Station08").transform;

        var brick=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Brick.mat");

        var cream=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Limestone.mat");

        var concrete=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Concrete.mat");

        var steel=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Steel.mat");

        var glass=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/Glazing.mat");

        var red=AssetDatabase.LoadAssetAtPath<Material>("Assets/HTownSim/Generated/ApparatusRed.mat");

        void Box(string name,(Vector3 Position,Vector3 Scale,Material Material) spec)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);

            go.name=name;

            go.transform.SetParent(root,false);

            go.transform.position=spec.Position;

            go.transform.localScale=spec.Scale;

            go.GetComponent<Renderer>().sharedMaterial=spec.Material;
        }

        Box("StationApron",(new Vector3(4,0.08f,-57),new Vector3(32,0.15f,17),concrete));

        Box("BayFloor",(new Vector3(5,0.08f,-71),new Vector3(15,0.15f,14),concrete));

        Box("RearWall",(new Vector3(5,2.8f,-78),new Vector3(16,5.6f,0.5f),brick));

        Box("LeftWall",(new Vector3(-3,2.8f,-71),new Vector3(0.6f,5.6f,14),brick));

        Box("RightWall",(new Vector3(13,2.8f,-71),new Vector3(0.6f,5.6f,14),brick));

        Box("CenterPillar",(new Vector3(5,2.4f,-64),new Vector3(0.6f,4.8f,0.6f),brick));

        Box("Fascia",(new Vector3(5,5.3f,-64),new Vector3(16,1.2f,0.7f),red));

        Box("Roof",(new Vector3(5,5.95f,-71),new Vector3(16.5f,0.3f,14.5f),cream));

        Box("StationOffice",(new Vector3(-9,2.4f,-71),new Vector3(11,4.8f,14),brick));

        Box("OfficeRoof",(new Vector3(-9,4.9f,-71),new Vector3(11.5f,0.25f,14.5f),cream));

        for(var x=-12;x<=-6;x+=3)
        {
            Box("OfficeWindow",(new Vector3(x,2.5f,-63.94f),new Vector3(2,1.8f,0.08f),glass));
        }

        foreach(var x in new[]{1f,9f})
        {
            Box("OpenBayDoor",(new Vector3(x,4.7f,-64),new Vector3(6.8f,0.25f,0.3f),steel));

            Box("ApronGuide",(new Vector3(x-2.4f,0.17f,-57),new Vector3(0.12f,0.02f,12),cream));

            Box("ApronGuide",(new Vector3(x+2.4f,0.17f,-57),new Vector3(0.12f,0.02f,12),cream));
        }

        var sign=new GameObject("StationName").AddComponent<TextMeshPro>();

        sign.transform.SetParent(root,false);

        sign.transform.position=new Vector3(5,5.3f,-63.6f);

        sign.transform.rotation=Quaternion.Euler(0,180,0);

        sign.text="H T O W N   /   FIRE STATION 08";

        sign.fontSize=7;

        sign.fontStyle=FontStyles.Bold;

        sign.characterSpacing=2;

        sign.alignment=TextAlignmentOptions.Center;

        sign.rectTransform.sizeDelta=new Vector2(15,1);

        sign.color=cream.color;

        GameObject.Find("Engine08").transform.SetPositionAndRotation(new Vector3(9,0.2f,-54),Quaternion.identity);

        var volume=GameObject.Find("DuskGrade").GetComponent<Volume>();

        var profile=volume.sharedProfile;

        profile.components.RemoveAll(c=>c==null);

        if(!profile.Has<Tonemapping>()) { profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES); }

        if(!profile.Has<Bloom>()) { profile.Add<Bloom>().intensity.Override(0.3f); }

        if(!profile.Has<Vignette>()) { profile.Add<Vignette>().intensity.Override(0.2f); }

        if(!profile.Has<ColorAdjustments>()) { profile.Add<ColorAdjustments>().contrast.Override(8f); }

        foreach(var component in profile.components)
        {
            if(!AssetDatabase.Contains(component))
            {
                AssetDatabase.AddObjectToAsset(component,profile);
            }
        }

        EditorUtility.SetDirty(profile);

        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        return "Station 08 added. Engine 08 starts on its apron and resets there.";
    }
}
