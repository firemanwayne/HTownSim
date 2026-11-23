#nullable enable
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds street-level detail without touching the street grid or building footprints: a named sign board over every shop
/// storefront, varied awning colours, "OPEN" signs, and parking-lot furniture (light poles, pylon signs, signs, dumpsters).
/// Run it again after rebuilding the city with BuildRealMidtown. It removes its previous output first.
/// </summary>
public static class AddCityDetail
{
    private const string SignRoot = "BusinessSigns";
    private const string LotRoot = "LotDetail";
    private const string MaterialDir = "Assets/HTownSim/Generated/";
    private const float BoardHeight = 1.25f;
    private const float BoardDepth = 0.16f;
    private const float BoardY = 4.25f;
    private const float TextLift = 0.09f;
    private const float MinBoard = 3f;
    private const float MaxBoard = 8f;
    private const float PoleHeight = 7f;
    private const float PylonHeight = 5.5f;
    private const float PylonRadius = 45f;
    private const float ClearRadius = 0.9f;

    private static readonly string[] Names =
    {
        "TAQUERIA EL SOL", "MIDTOWN PHARMACY", "BAYOU CITY COFFEE", "ALLEN'S BARBER SHOP", "ELGIN LAUNDROMAT", "LUCKY DRAGON CHINESE",
        "SLIM'S PIZZA", "H-TOWN WIRELESS", "GULF COAST HARDWARE", "LA FRESCA ICE CREAM", "FANNIN DRY CLEANERS", "TEJANO BOOTS & HATS",
        "THIRD WARD SOUL FOOD", "TEXAS PAWN & LOAN", "CUATRO AUTO REPAIR", "MI PUEBLO MARKET", "BAYOU BOOKS", "RODEO CUTS",
        "CRAWFISH SHACK", "MIDTOWN DENTAL", "PALMS LIQUOR", "SUNRISE DONUTS", "CASA LUNA CAFE", "BIG TEX BURGERS",
        "HOLMAN TIRE & LUBE", "JACKSON FLORIST", "GREEN LEAF VITAMINS", "PHO SAIGON", "EL REY TACOS", "CORNER FOOD MART",
    };

    private static readonly Color[] BoardColours =
    {
        new Color(0.12f, 0.2f, 0.45f), new Color(0.6f, 0.1f, 0.1f), new Color(0.08f, 0.38f, 0.22f), new Color(0.16f, 0.16f, 0.19f),
        new Color(0.40f, 0.25f, 0.06f), new Color(0.30f, 0.10f, 0.36f), new Color(0.05f, 0.30f, 0.34f), new Color(0.55f, 0.42f, 0.08f),
    };

    private static readonly Color[] AwningColours =
    {
        new Color(0.66f, 0.09f, 0.08f), new Color(0.10f, 0.25f, 0.50f), new Color(0.07f, 0.38f, 0.20f), new Color(0.75f, 0.58f, 0.10f),
        new Color(0.18f, 0.18f, 0.20f), new Color(0.50f, 0.18f, 0.40f),
    };

    private static Material[] _boardMaterials = new Material[0];
    private static Material[] _awningMaterials = new Material[0];
    private static Material _pole = null!;
    private static Material _lampHead = null!;
    private static Material _dumpster = null!;
    private static Material _signBlue = null!;
    private static Material _openSign = null!;

    public static string Run()
    {
        Clear();

        var lit = Shader.Find("Universal Render Pipeline/Lit");

        _boardMaterials = MakeMaterials("SignBoard", BoardColours, lit, 0.35f);

        _awningMaterials = MakeMaterials("AwningColour", AwningColours, lit, 0.2f);

        _pole = MakeMaterial("LotPole", new Color(0.32f, 0.34f, 0.37f), lit, 0.4f);

        _lampHead = MakeMaterial("LotLampHead", new Color(1f, 0.85f, 0.55f), lit, 0.2f);

        _lampHead.EnableKeyword("_EMISSION");

        _lampHead.SetColor("_EmissionColor", new Color(2.2f, 1.7f, 0.9f));

        _dumpster = MakeMaterial("Dumpster", new Color(0.12f, 0.33f, 0.2f), lit, 0.3f);

        _signBlue = MakeMaterial("LotSignBlue", new Color(0.05f, 0.2f, 0.55f), lit, 0.3f);

        _openSign = MakeMaterial("OpenSignBoard", new Color(0.03f, 0.03f, 0.04f), lit, 0.5f);

        var signs = new GameObject(SignRoot).transform;

        var lots = new GameObject(LotRoot).transform;

        var shops = FindShops();

        var signCount = 0;

        var shopIndex = 0;

        foreach (var shop in shops)
        {
            if (AddShopSign(shop, signs, shopIndex))
            {
                signCount++;
            }

            shopIndex++;
        }

        var lotCount = AddLots(lots, shops);

        var scene = signs.gameObject.scene;

        EditorSceneManager.MarkSceneDirty(scene);

        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();

        return "signs=" + signCount + " of " + shops.Count + " shops, lots detailed=" + lotCount;
    }

    private static void Clear()
    {
        foreach (var name in new[] { SignRoot, LotRoot })
        {
            var old = GameObject.Find(name);

            if (old != null)
            {
                Object.DestroyImmediate(old);
            }
        }
    }

    private static Material MakeMaterial(string name, Color colour, Shader shader, float smoothness)
    {
        var path = MaterialDir + name + ".mat";

        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader) { name = name };

            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;

        material.SetColor("_BaseColor", colour);

        material.SetFloat("_Smoothness", smoothness);

        EditorUtility.SetDirty(material);

        return material;
    }

    private static Material[] MakeMaterials(string prefix, Color[] colours, Shader shader, float smoothness)
    {
        var result = new Material[colours.Length];

        for (var i = 0; i < colours.Length; i++)
        {
            result[i] = MakeMaterial(prefix + i, colours[i], shader, smoothness);
        }

        return result;
    }

    private static List<Transform> FindShops()
    {
        var shops = new List<Transform>();

        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (transform.name.StartsWith("Shop_") && transform.Find("Storefront") != null && transform.Find("Body") != null)
            {
                shops.Add(transform);
            }
        }

        shops.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        return shops;
    }

    private static string NameFor(int index)
    {
        return Names[(index * 7 + index / Names.Length * 3) % Names.Length];
    }

    private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);

        go.name = name;

        Object.DestroyImmediate(go.GetComponent<Collider>());

        go.transform.SetParent(parent, true);

        go.transform.position = position;

        go.transform.localScale = scale;

        go.GetComponent<Renderer>().sharedMaterial = material;

        go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return go;
    }

    private static TextMeshPro Text(string name, Transform parent, Vector3 position, Vector3 outward, string text, Vector2 size, Color colour, float maxSize)
    {
        var go = new GameObject(name);

        go.transform.SetParent(parent, true);

        go.transform.position = position;

        go.transform.rotation = Quaternion.LookRotation(-outward);

        var tmp = go.AddComponent<TextMeshPro>();

        tmp.text = text;

        tmp.alignment = TextAlignmentOptions.Center;

        tmp.color = colour;

        tmp.fontStyle = FontStyles.Bold;

        tmp.enableAutoSizing = true;

        tmp.fontSizeMin = 0.5f;

        tmp.fontSizeMax = maxSize;

        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        tmp.rectTransform.sizeDelta = size;

        return tmp;
    }

    /// <summary>Adds the sign board, optional OPEN sign and a recoloured awning for one shop. Returns false if the storefront faces no clear direction.</summary>
    private static bool AddShopSign(Transform shop, Transform parent, int index)
    {
        var storefront = shop.Find("Storefront");

        var body = shop.Find("Body");

        var scale = storefront.lossyScale;

        var alongZ = scale.z < scale.x;

        var width = alongZ ? scale.x : scale.z;

        var delta = storefront.position - body.position;

        var outward = alongZ ? new Vector3(0f, 0f, Mathf.Sign(delta.z)) : new Vector3(Mathf.Sign(delta.x), 0f, 0f);

        var boardWidth = Mathf.Clamp(width * 0.55f, MinBoard, MaxBoard);

        var colourIndex = (index * 5 + 2) % _boardMaterials.Length;

        var centre = new Vector3(storefront.position.x, BoardY, storefront.position.z) + outward * 0.22f;

        var board = Prim(PrimitiveType.Cube, "BusinessSign", parent, centre, alongZ ? new Vector3(boardWidth, BoardHeight, BoardDepth) : new Vector3(BoardDepth, BoardHeight, boardWidth), _boardMaterials[colourIndex]);

        board.transform.rotation = Quaternion.identity;

        var textColour = colourIndex == 7 ? new Color(0.1f, 0.08f, 0.02f) : new Color(1f, 0.95f, 0.82f);

        Text("BusinessSignText", parent, centre + outward * (BoardDepth / 2f + TextLift / 10f), outward, NameFor(index), new Vector2(boardWidth - 0.4f, BoardHeight - 0.2f), textColour, 4.5f);

        var awning = shop.Find("Awning");

        if (awning != null)
        {
            awning.GetComponent<Renderer>().sharedMaterial = _awningMaterials[(index * 3 + 1) % _awningMaterials.Length];
        }

        if (index % 2 == 0)
        {
            var lateral = alongZ ? Vector3.right : Vector3.forward;

            var openPosition = new Vector3(storefront.position.x, 2.3f, storefront.position.z) + outward * 0.1f + lateral * (width * 0.3f);

            var openBoard = Prim(PrimitiveType.Cube, "OpenSign", parent, openPosition, alongZ ? new Vector3(1.3f, 0.5f, 0.06f) : new Vector3(0.06f, 0.5f, 1.3f), _openSign);

            Text("OpenSignText", parent, openPosition + outward * 0.04f, outward, "OPEN", new Vector2(1.2f, 0.45f), new Color(1f, 0.15f, 0.1f), 2.2f);
        }

        return true;
    }

    private static bool Blocked(Vector3 position)
    {
        foreach (var hit in Physics.OverlapSphere(position + Vector3.up * 1f, ClearRadius))
        {
            var name = hit.name;

            if (name.Contains("Slab") || name.Contains("Lot") || name == "Streets" || name == "Ground" || name.Contains("Stall"))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>The topmost surface under a point is the lot's own surface, not a road or sidewalk.</summary>
    private static bool OnLot(Vector3 position)
    {
        var topY = -999f;

        var topName = string.Empty;

        foreach (var hit in Physics.RaycastAll(new Vector3(position.x, 4f, position.z), Vector3.down, 8f))
        {
            if (hit.point.y > topY)
            {
                topY = hit.point.y;

                topName = hit.collider.name;
            }
        }

        return topName != "Streets";
    }

    private static int AddLots(Transform parent, List<Transform> shops)
    {
        var lotsDetailed = 0;

        var seen = new HashSet<Transform>();

        foreach (var line in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (line.name == "StallLine" && line.parent != null)
            {
                seen.Add(line.parent);
            }
        }

        var lotIndex = 0;

        foreach (var lot in seen)
        {
            var bounds = new Bounds();

            var first = true;

            foreach (Transform child in lot)
            {
                if (child.name != "StallLine")
                {
                    continue;
                }

                var r = child.GetComponent<Renderer>();

                if (r == null)
                {
                    continue;
                }

                if (first)
                {
                    bounds = r.bounds;

                    first = false;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (first || bounds.size.x < 6f || bounds.size.z < 6f)
            {
                continue;
            }

            if (AddLotProps(parent, bounds, shops, lotIndex))
            {
                lotsDetailed++;
            }

            lotIndex++;
        }

        return lotsDetailed;
    }

    private static bool AddLotProps(Transform parent, Bounds bounds, List<Transform> shops, int lotIndex)
    {
        var corners = new[]
        {
            new Vector3(bounds.min.x - 1f, 0.2f, bounds.min.z - 1f), new Vector3(bounds.max.x + 1f, 0.2f, bounds.max.z + 1f),
            new Vector3(bounds.min.x - 1f, 0.2f, bounds.max.z + 1f), new Vector3(bounds.max.x + 1f, 0.2f, bounds.min.z - 1f),
        };

        var added = 0;

        foreach (var corner in corners)
        {
            if (added >= 2 || !OnLot(corner) || Blocked(corner))
            {
                continue;
            }

            Prim(PrimitiveType.Cylinder, "LotPole", parent, corner + Vector3.up * (PoleHeight / 2f), new Vector3(0.22f, PoleHeight / 2f, 0.22f), _pole);

            var toCentre = (bounds.center - corner);

            toCentre.y = 0f;

            var arm = toCentre.normalized;

            Prim(PrimitiveType.Cube, "LotPoleArm", parent, corner + Vector3.up * PoleHeight + arm * 0.9f, new Vector3(Mathf.Abs(arm.x) * 1.8f + 0.12f, 0.12f, Mathf.Abs(arm.z) * 1.8f + 0.12f), _pole);

            Prim(PrimitiveType.Cube, "LotLamp", parent, corner + Vector3.up * (PoleHeight - 0.08f) + arm * 1.7f, new Vector3(0.7f, 0.14f, 0.5f), _lampHead);

            added++;
        }

        var shop = NearestShop(bounds.center, shops, out var shopIndex);

        if (shop != null && added > 0)
        {
            AddPylon(parent, bounds, shopIndex, lotIndex);
        }

        AddDumpster(parent, bounds);

        AddParkingSign(parent, bounds);

        return added > 0;
    }

    private static Transform? NearestShop(Vector3 point, List<Transform> shops, out int index)
    {
        index = -1;

        Transform? best = null;

        var bestDistance = PylonRadius;

        for (var i = 0; i < shops.Count; i++)
        {
            var body = shops[i].Find("Body");

            var d = Vector2.Distance(new Vector2(body.position.x, body.position.z), new Vector2(point.x, point.z));

            if (d < bestDistance)
            {
                bestDistance = d;

                best = shops[i];

                index = i;
            }
        }

        return best;
    }

    private static void AddPylon(Transform parent, Bounds bounds, int shopIndex, int lotIndex)
    {
        var outward = lotIndex % 2 == 0 ? Vector3.right : Vector3.forward;

        var position = new Vector3(bounds.center.x, 0.2f, bounds.center.z) + new Vector3(bounds.extents.x * (outward == Vector3.right ? 1f : 0f), 0f, bounds.extents.z * (outward == Vector3.forward ? 1f : 0f)) + outward * 1.2f;

        if (!OnLot(position) || Blocked(position))
        {
            return;
        }

        Prim(PrimitiveType.Cylinder, "PylonPole", parent, position + Vector3.up * (PylonHeight / 2f), new Vector3(0.28f, PylonHeight / 2f, 0.28f), _pole);

        var boardCentre = position + Vector3.up * (PylonHeight + 0.5f);

        var colourIndex = (shopIndex * 5 + 2) % _boardMaterials.Length;

        var scale = outward == Vector3.right ? new Vector3(0.2f, 1.7f, 3.2f) : new Vector3(3.2f, 1.7f, 0.2f);

        var board = Prim(PrimitiveType.Cube, "PylonSign", parent, boardCentre, scale, _boardMaterials[colourIndex]);

        var colour = colourIndex == 7 ? new Color(0.1f, 0.08f, 0.02f) : new Color(1f, 0.95f, 0.82f);

        Text("PylonText", parent, boardCentre + outward * 0.11f, outward, NameFor(shopIndex), new Vector2(3f, 1.4f), colour, 1.6f);

        Text("PylonTextBack", parent, boardCentre - outward * 0.11f, -outward, NameFor(shopIndex), new Vector2(3f, 1.4f), colour, 1.6f);
    }

    private static void AddDumpster(Transform parent, Bounds bounds)
    {
        var spot = new Vector3(bounds.max.x - 1.5f, 0.2f, bounds.min.z - 2.4f);

        if (!OnLot(spot) || Blocked(spot))
        {
            return;
        }

        Prim(PrimitiveType.Cube, "Dumpster", parent, spot + Vector3.up * 0.7f, new Vector3(2.4f, 1.4f, 1.6f), _dumpster);

        Prim(PrimitiveType.Cube, "DumpsterLid", parent, spot + Vector3.up * 1.45f, new Vector3(2.5f, 0.1f, 1.7f), _pole);
    }

    private static void AddParkingSign(Transform parent, Bounds bounds)
    {
        var spot = new Vector3(bounds.min.x - 1.2f, 0.2f, bounds.center.z);

        if (!OnLot(spot) || Blocked(spot))
        {
            return;
        }

        Prim(PrimitiveType.Cylinder, "SignPost", parent, spot + Vector3.up * 1.2f, new Vector3(0.08f, 1.2f, 0.08f), _pole);

        var board = Prim(PrimitiveType.Cube, "ParkingSign", parent, spot + Vector3.up * 2.5f, new Vector3(0.06f, 0.9f, 0.7f), _signBlue);

        Text("ParkingSignText", parent, spot + Vector3.up * 2.5f - Vector3.right * 0.05f, -Vector3.right, "P", new Vector2(0.6f, 0.8f), Color.white, 2f);
    }
}
