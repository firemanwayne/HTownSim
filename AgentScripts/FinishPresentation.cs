using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Finishes effect transparency and lighting after the gameplay review.</summary>
public static class FinishPresentation
{
    /// <summary>Replaces square particle silhouettes with soft radial sprites.</summary>
    public static string Apply()
    {
        const string path="Assets/HTownSim/Generated/SoftParticle.asset";

        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if(texture==null)
        {
            const int size=64;

            texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="SoftParticle",wrapMode=TextureWrapMode.Clamp};

            var pixels=new Color[size*size];

            for(var y=0;y<size;y++)
            {
                for(var x=0;x<size;x++)
                {
                    var radius=new Vector2((x+0.5f)/size*2-1,(y+0.5f)/size*2-1).magnitude;

                    var alpha=Mathf.Pow(Mathf.Clamp01(1-radius),1.8f);

                    pixels[y*size+x]=new Color(1,1,1,alpha);
                }
            }

            texture.SetPixels(pixels);

            texture.Apply();

            AssetDatabase.CreateAsset(texture,path);
        }

        foreach(var name in new[]{"Flames","Smoke"})
        {
            var system=GameObject.Find(name).GetComponent<ParticleSystem>();

            system.GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture=texture;

            var overLifetime=system.colorOverLifetime;

            overLifetime.enabled=true;

            var gradient=new Gradient();

            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(0.85f,0.15f),new GradientAlphaKey(0,1)});

            overLifetime.color=gradient;
        }

        var profile=GameObject.Find("DuskGrade").GetComponent<Volume>().sharedProfile;

        profile.TryGet<ColorAdjustments>(out var color);

        color.postExposure.Override(0.65f);

        EditorUtility.SetDirty(color);

        GameObject.Find("DuskSun").GetComponent<Light>().intensity=1.5f;

        var station=GameObject.Find("Station08").transform;

        var sign=station.Find("StationName").GetComponent<TMP_Text>();

        sign.transform.position=new Vector3(1,11,-63.45f);

        sign.fontSize=5.5f;

        sign.rectTransform.sizeDelta=new Vector2(23,0.55f);

        foreach(var t in Object.FindObjectsByType<TMP_Text>())
        {
            if(t.text.StartsWith("WASD"))
            {
                t.rectTransform.sizeDelta=new Vector2(1400,76);
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        AssetDatabase.SaveAssets();

        return "Soft fire/smoke sprites, brighter dusk exposure and control layout saved.";
    }
}
