using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-time authoring pass that lays out the Midtown Houston street grid around Station 7, rotated so the
/// NE-SW streets (Austin, La Branch, Caroline) run along +Z like Canal Street and the NW-SE streets
/// (Elgin, Rosalie) run along +X. +Z is therefore north-east and +X is south-east. It replaces the earlier
/// TravisDistrict pass and adds mixed-use blocks, parks, surface lots, a METRORail median on Caroline Street
/// and the elevated freeway that runs beside the real station. Everything lives under <c>MidtownDistrict</c>.
/// </summary>
public static class AddMidtown
{
    private const string DistrictName = "MidtownDistrict";

    private const string OldDistrictName = "TravisDistrict";

    private const string MaterialFolder = "Assets/HTownSim/Generated/";

    private const float SlabThickness = 0.12f;

    private const float SidewalkY = 0.16f;

    private const float PodiumHeight = 3.5f;

    private const float CrossingZ = 45f;

    private const float RosalieZ = 130f;

    private const float AnitaZ = 220f;

    private const float TuamZ = 310f;

    private const float SanJacintoX = -175f;

    private const float WorldMinX = -215f;

    private const float WorldMaxX = 180f;

    private const float WorldMinZ = -80f;

    private const float WorldMaxZ = 350f;

    private const float PreviousNorthEdge = 190f;

    private const float FreewayX = 150f;

    private const StaticEditorFlags StaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;

    private static Material _asphalt, _concrete, _limestone, _glass, _steel, _dark, _leaf, _amber, _brick, _red;

    private static Material _towerGlass, _granite, _yellow, _white, _signalRed, _signalGreen, _wood, _park, _stucco, _sage;

    private static Material[] _carMaterials;

    private static Material[] _shopMaterials;

    private static Transform _district;

    private static Transform _parent;

    private static int _shopIndex;

    private static int _apartmentIndex;

    /// <summary>Kinds of street cross section.</summary>
    private enum StreetKind
    {
        TwoWay,
        OneWay,
        Rail,
    }

    /// <summary>Visual treatment of a building.</summary>
    private enum BuildingStyle
    {
        Glass,
        Striped,
        Garage,
        Shop,
        Apartment,
    }

    /// <summary>A street that crosses another, as a centre position and half width along the crossed street.</summary>
    private readonly struct Crossing
    {
        public Crossing(float centre, float halfWidth)
        {
            Centre = centre;

            HalfWidth = halfWidth;
        }

        public float Centre { get; }

        public float HalfWidth { get; }
    }

    /// <summary>Describes one primitive to create under the current parent.</summary>
    private readonly struct Spec
    {
        public Spec(string name, Vector3 position, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Cube, bool collide = false)
        {
            Name = name;

            Position = position;

            Scale = scale;

            Material = material;

            Shape = shape;

            Collide = collide;
        }

        public string Name { get; }

        public Vector3 Position { get; }

        public Vector3 Scale { get; }

        public Material Material { get; }

        public PrimitiveType Shape { get; }

        public bool Collide { get; }
    }

    /// <summary>Describes one street. Ranges are (from, to) along the street's axis.</summary>
    private readonly struct Street
    {
        public Street(string name, bool alongZ, float fixedPosition, float halfWidth, StreetKind kind)
        {
            Name = name;

            AlongZ = alongZ;

            Fixed = fixedPosition;

            HalfWidth = halfWidth;

            Kind = kind;

            Y = 0f;

            SlabRange = Vector2.zero;

            FeatureRange = Vector2.zero;

            SidewalkWidth = 5f;

            Crossings = new Crossing[0];

            DrawSlab = true;

            DrawFeatures = true;

            PositiveLegsOnly = false;
        }

        private Street(Street source, float y, Vector2 slab, Vector2 features, float sidewalk, Crossing[] crossings, bool drawSlab, bool drawFeatures, bool positiveOnly)
        {
            Name = source.Name;

            AlongZ = source.AlongZ;

            Fixed = source.Fixed;

            HalfWidth = source.HalfWidth;

            Kind = source.Kind;

            Y = y;

            SlabRange = slab;

            FeatureRange = features;

            SidewalkWidth = sidewalk;

            Crossings = crossings;

            DrawSlab = drawSlab;

            DrawFeatures = drawFeatures;

            PositiveLegsOnly = positiveOnly;
        }

        public string Name { get; }

        public bool AlongZ { get; }

        public float Fixed { get; }

        public float HalfWidth { get; }

        public StreetKind Kind { get; }

        public float Y { get; }

        public Vector2 SlabRange { get; }

        public Vector2 FeatureRange { get; }

        public float SidewalkWidth { get; }

        public Crossing[] Crossings { get; }

        public bool DrawSlab { get; }

        public bool DrawFeatures { get; }

        public bool PositiveLegsOnly { get; }

        /// <summary>Returns a copy with the layout details filled in.</summary>
        public Street With(float y, Vector2 slab, Vector2 features, float sidewalk, Crossing[] crossings)
        {
            return new Street(this, y, slab, features, sidewalk, crossings, DrawSlab, DrawFeatures, PositiveLegsOnly);
        }

        /// <summary>Returns a copy that draws only its crosswalks (an already-built street).</summary>
        public Street ExistingPositiveLegs()
        {
            return new Street(this, Y, SlabRange, FeatureRange, SidewalkWidth, Crossings, false, false, true);
        }

        /// <summary>Returns a copy whose slab already exists.</summary>
        public Street ExistingSlab()
        {
            return new Street(this, Y, SlabRange, FeatureRange, SidewalkWidth, Crossings, false, true, false);
        }
    }

    /// <summary>Describes one building.</summary>
    private readonly struct BuildingSpec
    {
        public BuildingSpec(string name, Vector2 centre, Vector3 size, BuildingStyle style, Vector3 facing)
        {
            Name = name;

            Centre = centre;

            Size = size;

            Style = style;

            Facing = facing;
        }

        public string Name { get; }

        public Vector2 Centre { get; }

        public Vector3 Size { get; }

        public BuildingStyle Style { get; }

        public Vector3 Facing { get; }
    }

    /// <summary>Adds the Midtown layout and returns a summary.</summary>
    public static string Build()
    {
        if (EditorApplication.isPlaying)
        {
            return "Exit Play Mode first.";
        }

        foreach (var stale in new[] { OldDistrictName, DistrictName })
        {
            var old = GameObject.Find(stale);

            if (old != null)
            {
                Object.DestroyImmediate(old);
            }
        }

        _shopIndex = 0;

        _apartmentIndex = 0;

        _district = new GameObject(DistrictName).transform;

        LoadMaterials();

        WidenWorld();

        var streets = LayOutStreets();

        foreach (var street in streets)
        {
            BuildStreet(street);
        }

        BuildSignals();

        BuildBlocks();

        BuildFreeway();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        return "Added " + _district.GetComponentsInChildren<Transform>().Length + " objects under " + DistrictName;
    }

    /// <summary>Loads or creates the shared materials.</summary>
    private static void LoadMaterials()
    {
        _asphalt = Mat("Asphalt", "303D43");

        _concrete = Mat("Concrete", "AAA697");

        _limestone = Mat("Limestone", "E6D7B7");

        _glass = Mat("Glazing", "284B59");

        _steel = Mat("Steel", "71858C");

        _dark = Mat("Rubber", "18282F");

        _leaf = Mat("LiveOak", "536D50");

        _amber = Mat("Amber", "FFAF52");

        _brick = Mat("Brick", "875946");

        _red = Mat("ApparatusRed", "B52E32");

        _towerGlass = Mat("TowerGlass", "6C93A8");

        _towerGlass.SetFloat("_Smoothness", 0.85f);

        _granite = Mat("Granite", "6E625B");

        _yellow = Mat("PaintYellow", "E0B83A");

        _white = Mat("PaintWhite", "DAD8CC");

        _signalRed = Mat("SignalRed", "E03A3A");

        _signalGreen = Mat("SignalGreen", "3AD17A");

        _wood = Mat("UtilityWood", "5A4636");

        _park = Mat("ParkGrass", "6A8F58");

        _stucco = Mat("Stucco", "C9B79C");

        _sage = Mat("SageStucco", "A9B49A");

        _carMaterials = new[] { Mat("CarWhite", "E9E9E6"), Mat("CarBlack", "1E2226"), Mat("CarSilver", "9AA3A8"), Mat("CarBlue", "2F4A6E"), Mat("CarRed", "9A2F2F"), Mat("CarTan", "B3A184") };

        _shopMaterials = new[] { _brick, _limestone, _concrete, _stucco, _sage };
    }

    /// <summary>Returns the named material from the generated folder, creating it if needed.</summary>
    private static Material Mat(string name, string hex)
    {
        var path = MaterialFolder + name + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        ColorUtility.TryParseHtmlString("#" + hex, out var colour);

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = colour };

        AssetDatabase.CreateAsset(material, path);

        return material;
    }

    /// <summary>Enlarges the ground and cross street and moves the distant skyline back so the grid fits.</summary>
    private static void WidenWorld()
    {
        var ground = GameObject.Find("Ground");

        if (ground != null)
        {
            ground.transform.position = new Vector3((WorldMinX + WorldMaxX) / 2f, -0.3f, (WorldMinZ + WorldMaxZ) / 2f);

            ground.transform.localScale = new Vector3(WorldMaxX - WorldMinX, 0.5f, WorldMaxZ - WorldMinZ);
        }

        var cross = GameObject.Find("CrossStreet");

        if (cross != null)
        {
            cross.transform.position = new Vector3((WorldMinX + WorldMaxX) / 2f, 0.01f, CrossingZ);

            cross.transform.localScale = new Vector3(WorldMaxX - WorldMinX, SlabThickness, 20f);
        }

        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "DistantSkyline" && root.transform.position.z < WorldMaxZ)
            {
                root.transform.position += new Vector3(0f, 0f, WorldMaxZ - PreviousNorthEdge);
            }
        }
    }

    /// <summary>Defines every street of the new grid.</summary>
    private static Street[] LayOutStreets()
    {
        var eastWest = new[] { new Crossing(SanJacintoX, 9f), new Crossing(-85f, 9f), new Crossing(0f, 11f), new Crossing(90f, 6f) };

        var northSouth = new[] { new Crossing(CrossingZ, 10f), new Crossing(RosalieZ, 8f), new Crossing(AnitaZ, 8f), new Crossing(TuamZ, 8f) };

        var slab = new Vector2(WorldMinX, WorldMaxX);

        var features = new Vector2(WorldMinX, 128f);

        var run = new Vector2(WorldMinZ, WorldMaxZ);

        var elgin = new Street("Elgin", false, CrossingZ, 10f, StreetKind.TwoWay).With(0.01f, slab, features, 5f, eastWest).ExistingSlab();

        var rosalie = new Street("Rosalie", false, RosalieZ, 8f, StreetKind.TwoWay).With(0.02f, slab, features, 5f, eastWest);

        var anita = new Street("Anita", false, AnitaZ, 8f, StreetKind.TwoWay).With(0.02f, slab, features, 5f, eastWest);

        var tuam = new Street("Tuam", false, TuamZ, 8f, StreetKind.TwoWay).With(0.02f, slab, features, 5f, eastWest);

        var laBranch = new Street("LaBranch", true, 90f, 6f, StreetKind.OneWay).With(0.03f, run, run, 4.5f, northSouth);

        var caroline = new Street("Caroline", true, -85f, 9f, StreetKind.Rail).With(0.04f, run, run, 5f, northSouth);

        var sanJacinto = new Street("SanJacinto", true, SanJacintoX, 9f, StreetKind.TwoWay).With(0.05f, run, run, 5f, northSouth);

        var canalNorth = new Street("CanalNorth", true, 0f, 11f, StreetKind.TwoWay).With(0f, new Vector2(95f, WorldMaxZ), new Vector2(105f, WorldMaxZ), 6f, new[] { new Crossing(RosalieZ, 8f), new Crossing(AnitaZ, 8f), new Crossing(TuamZ, 8f) });

        var canal = new Street("Canal", true, 0f, 11f, StreetKind.TwoWay).With(0f, new Vector2(-75f, 95f), new Vector2(-75f, 95f), 6f, new[] { new Crossing(CrossingZ, 10f) }).ExistingPositiveLegs();

        return new[] { elgin, rosalie, anita, tuam, laBranch, caroline, sanJacinto, canalNorth, canal };
    }

    /// <summary>Builds one street: slab, markings, sidewalks, trees, lamps and crosswalks.</summary>
    private static void BuildStreet(Street street)
    {
        Section(street.Name, _district);

        var top = street.Y + SlabThickness / 2f;

        if (street.DrawSlab)
        {
            var length = street.SlabRange.y - street.SlabRange.x;

            var centre = street.SlabRange.x + length / 2f;

            Make(new Spec(street.Name + "Road", Place(street, centre, 0f, street.Y), Size(street, length, street.HalfWidth * 2f, SlabThickness), _asphalt, PrimitiveType.Cube, true));
        }

        if (street.DrawFeatures)
        {
            BuildMarkings(street, top);

            BuildSidewalks(street);

            BuildFurniture(street);
        }

        BuildCrosswalks(street, top);
    }

    /// <summary>Paints the centre markings (and, for Caroline Street, lays the rail median).</summary>
    private static void BuildMarkings(Street street, float top)
    {
        var y = top + 0.012f;

        foreach (var span in Spans(street.FeatureRange, street.Crossings))
        {
            var length = span.y - span.x;

            var centre = span.x + length / 2f;

            switch (street.Kind)
            {
                case StreetKind.TwoWay:
                    foreach (var offset in new[] { -0.15f, 0.15f })
                    {
                        Make(new Spec("CenterLine", Place(street, centre, offset, y), Size(street, length, 0.12f, 0.02f), _yellow));
                    }

                    break;

                case StreetKind.OneWay:
                    for (var along = span.x + 2f; along < span.y - 2f; along += 8f)
                    {
                        Make(new Spec("LaneDash", Place(street, along, 0f, y), Size(street, 3f, 0.14f, 0.02f), _white));
                    }

                    for (var along = span.x + 14f; along < span.y - 6f; along += 40f)
                    {
                        BuildArrow(street, along, y);
                    }

                    break;

                default:
                    Make(new Spec("RailBed", Place(street, centre, 0f, y), Size(street, length, 5.2f, 0.02f), _concrete));

                    foreach (var offset in new[] { -0.72f, 0.72f })
                    {
                        Make(new Spec("Rail", Place(street, centre, offset, y + 0.03f), Size(street, length, 0.12f, 0.06f), _steel));
                    }

                    break;
            }
        }
    }

    /// <summary>Paints a one-way arrow pointing along +Z.</summary>
    private static void BuildArrow(Street street, float along, float y)
    {
        Make(new Spec("ArrowShaft", Place(street, along, 0f, y), Size(street, 2.6f, 0.4f, 0.02f), _white));

        var left = Make(new Spec("ArrowHead", Place(street, along + 0.81f, -0.49f, y), Size(street, 1.4f, 0.4f, 0.02f), _white));

        left.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

        var right = Make(new Spec("ArrowHead", Place(street, along + 0.81f, 0.49f, y), Size(street, 1.4f, 0.4f, 0.02f), _white));

        right.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
    }

    /// <summary>Creates the sidewalks on both sides, stopping at each crossing road edge.</summary>
    private static void BuildSidewalks(Street street)
    {
        var offset = street.HalfWidth + street.SidewalkWidth / 2f;

        foreach (var side in new[] { -1f, 1f })
        {
            foreach (var span in Spans(street.FeatureRange, street.Crossings))
            {
                var length = span.y - span.x;

                Make(new Spec(street.Name + "Sidewalk", Place(street, span.x + length / 2f, side * offset, SidewalkY), Size(street, length, street.SidewalkWidth, 0.3f), _concrete, PrimitiveType.Cube, true));
            }
        }
    }

    /// <summary>Plants live oaks and places lamps along the sidewalks, away from the intersections.</summary>
    private static void BuildFurniture(Street street)
    {
        var treeOffset = street.HalfWidth + street.SidewalkWidth / 2f;

        var lampOffset = street.HalfWidth + 1.2f;

        for (var along = street.FeatureRange.x + 9f; along <= street.FeatureRange.y - 4f; along += 18f)
        {
            if (!NearCrossing(street, along))
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    Oak(Place(street, along, side * treeOffset, 0f));
                }
            }

            var lampAlong = along + 9f;

            if (lampAlong > street.FeatureRange.y - 4f || NearCrossing(street, lampAlong))
            {
                continue;
            }

            foreach (var side in new[] { -1f, 1f })
            {
                var towardStreet = street.AlongZ ? new Vector3(-side, 0f, 0f) : new Vector3(0f, 0f, -side);

                Lamp(Place(street, lampAlong, side * lampOffset, 0f), towardStreet);
            }
        }
    }

    /// <summary>Paints the zebra crossings on both sides of every crossing street.</summary>
    private static void BuildCrosswalks(Street street, float top)
    {
        var y = top + 0.012f;

        foreach (var crossing in street.Crossings)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                if (street.PositiveLegsOnly && side < 0f)
                {
                    continue;
                }

                var along = crossing.Centre + side * (crossing.HalfWidth + 2f);

                var width = street.HalfWidth * 2f;

                for (var offset = -width / 2f + 0.7f; offset <= width / 2f - 0.6f; offset += 1.4f)
                {
                    Make(new Spec("Crosswalk", Place(street, along, offset, y), Size(street, 3f, 0.7f, 0.02f), _white));
                }
            }
        }
    }

    /// <summary>Builds the mast-arm signals at the three Elgin Street intersections.</summary>
    private static void BuildSignals()
    {
        Section("Signals", _district);

        foreach (var corner in new[] { (X: SanJacintoX, Half: 9f), (X: -85f, Half: 9f), (X: 0f, Half: 11f), (X: 90f, Half: 6f) })
        {
            var offsetX = corner.Half + 2f;

            var offsetZ = 12f;

            var spanZ = 24f;

            Mast(new Vector3(corner.X - offsetX, 0f, CrossingZ - offsetZ), new Vector3(0f, 0f, spanZ));

            Mast(new Vector3(corner.X + offsetX, 0f, CrossingZ + offsetZ), new Vector3(0f, 0f, -spanZ));

            Mast(new Vector3(corner.X + offsetX, 0f, CrossingZ - offsetZ), new Vector3(-(corner.Half * 2f + 4f), 0f, 0f));
        }
    }

    /// <summary>Creates a mast-arm signal: a pole, a horizontal arm along <paramref name="arm"/>, and two signal heads.</summary>
    private static void Mast(Vector3 position, Vector3 arm)
    {
        const float ArmY = 7.6f;

        Make(new Spec("MastPole", position + Vector3.up * 4f, new Vector3(0.35f, 8f, 0.35f), _steel, PrimitiveType.Cylinder, true));

        var alongX = Mathf.Abs(arm.x) > Mathf.Abs(arm.z);

        var armScale = new Vector3(Mathf.Abs(arm.x) + 0.25f, 0.25f, Mathf.Abs(arm.z) + 0.25f);

        Make(new Spec("MastArm", position + arm / 2f + Vector3.up * ArmY, armScale, _steel));

        var faceOffset = alongX ? new Vector3(0f, 0f, 0.27f) : new Vector3(0.27f, 0f, 0f);

        var lensScale = alongX ? new Vector3(0.28f, 0.28f, 0.1f) : new Vector3(0.1f, 0.28f, 0.28f);

        foreach (var fraction in new[] { 0.45f, 0.8f })
        {
            var head = position + arm * fraction + Vector3.up * (ArmY - 0.9f);

            Make(new Spec("SignalHead", head, new Vector3(0.5f, 1.4f, 0.5f), _dark));

            foreach (var sign in new[] { -1f, 1f })
            {
                Make(new Spec("SignalRed", head + Vector3.up * 0.45f + faceOffset * sign, lensScale, _signalRed));

                Make(new Spec("SignalAmber", head + faceOffset * sign, lensScale, _amber));

                Make(new Spec("SignalGreen", head + Vector3.down * 0.45f + faceOffset * sign, lensScale, _signalGreen));
            }
        }
    }

    /// <summary>Lays out every block: shops, apartments, towers, surface lots and parks.</summary>
    private static void BuildBlocks()
    {
        Section("Blocks", _district);

        var blocks = _parent;

        var towardCaroline = Vector3.left;

        var buildings = new[]
        {
            new BuildingSpec("FarWestApartments", new Vector2(-112f, -40f), new Vector3(20f, 18f, 24f), BuildingStyle.Apartment, Vector3.right),
            new BuildingSpec("WestShopA", new Vector2(-58f, -52f), new Vector3(20f, 6f, 18f), BuildingStyle.Shop, towardCaroline),
            new BuildingSpec("WestShopB", new Vector2(-58f, -24f), new Vector3(20f, 7f, 20f), BuildingStyle.Shop, towardCaroline),
            new BuildingSpec("MidShopA", new Vector2(60f, -18f), new Vector3(28f, 6f, 22f), BuildingStyle.Shop, Vector3.right),
            new BuildingSpec("MidApartments", new Vector2(62f, -52f), new Vector3(30f, 15f, 24f), BuildingStyle.Apartment, Vector3.right),
            new BuildingSpec("FarWestApartmentsNorth", new Vector2(-112f, 92f), new Vector3(22f, 22f, 26f), BuildingStyle.Apartment, Vector3.right),
            new BuildingSpec("WestApartments", new Vector2(-56f, 92f), new Vector3(26f, 20f, 30f), BuildingStyle.Apartment, towardCaroline),
            new BuildingSpec("WestShopC", new Vector2(-28f, 108f), new Vector3(22f, 6f, 16f), BuildingStyle.Shop, Vector3.right),
            new BuildingSpec("MidApartmentsNorth", new Vector2(61f, 108f), new Vector3(34f, 18f, 16f), BuildingStyle.Apartment, Vector3.forward),
            new BuildingSpec("MidShopB", new Vector2(27f, 108f), new Vector3(22f, 5f, 16f), BuildingStyle.Shop, Vector3.forward),
            new BuildingSpec("EastApartments", new Vector2(116f, 90f), new Vector3(26f, 16f, 40f), BuildingStyle.Apartment, Vector3.left),
            new BuildingSpec("NorthWestTower", new Vector2(-50f, 168f), new Vector3(24f, 56f, 20f), BuildingStyle.Striped, Vector3.back),
            new BuildingSpec("NorthShopA", new Vector2(-27f, 152f), new Vector3(18f, 6f, 14f), BuildingStyle.Shop, Vector3.back),
            new BuildingSpec("NorthGlassTower", new Vector2(45f, 172f), new Vector3(24f, 50f, 22f), BuildingStyle.Glass, Vector3.back),
            new BuildingSpec("NorthGarage", new Vector2(71f, 160f), new Vector3(13f, 20f, 20f), BuildingStyle.Garage, Vector3.back),
            new BuildingSpec("NorthShopB", new Vector2(28f, 152f), new Vector3(20f, 6f, 14f), BuildingStyle.Shop, Vector3.back),
            new BuildingSpec("EastGlassTower", new Vector2(116f, 166f), new Vector3(24f, 46f, 22f), BuildingStyle.Glass, Vector3.left),
            new BuildingSpec("FarWestNorthApartments", new Vector2(-112f, 165f), new Vector3(22f, 24f, 26f), BuildingStyle.Apartment, Vector3.right),
        };

        foreach (var building in buildings)
        {
            Section(building.Name, blocks);

            BuildBuilding(building);
        }

        Section("Lots", blocks);

        var lots = _parent;

        Lot(lots, new Vector2(-112f, 5f), new Vector2(22f, 40f), 11);

        Lot(lots, new Vector2(-57f, 9f), new Vector2(26f, 36f), 23);

        Lot(lots, new Vector2(60f, 13f), new Vector2(36f, 30f), 37);

        Section("Parks", blocks);

        var parks = _parent;

        Park(parks, new Vector2(116f, -22f), new Vector2(30f, 100f));

        Park(parks, new Vector2(61f, 79f), new Vector2(35f, 34f));

        Section("Fill", blocks);

        var fill = _parent;

        var fillBlocks = new[]
        {
            new Rect(-161f, -75f, 36f, 103f), new Rect(-161f, 62f, 36f, 53f), new Rect(-161f, 145f, 36f, 60f),
            new Rect(-161f, 235f, 62f, 60f), new Rect(-161f, 325f, 62f, 23f),
            new Rect(-213f, -75f, 24f, 103f), new Rect(-213f, 62f, 24f, 53f), new Rect(-213f, 145f, 24f, 60f),
            new Rect(-213f, 235f, 24f, 60f), new Rect(-213f, 325f, 24f, 23f),
            new Rect(-69f, 193f, 50f, 12f), new Rect(-69f, 235f, 50f, 60f), new Rect(-69f, 325f, 50f, 23f),
            new Rect(19f, 235f, 58f, 60f), new Rect(19f, 325f, 58f, 23f),
            new Rect(103f, 235f, 26f, 60f), new Rect(103f, 325f, 26f, 23f),
        };

        for (var i = 0; i < fillBlocks.Length; i++)
        {
            FillBlock(fill, fillBlocks[i], 101 + i * 17);
        }

        BuildDistantSkyline();
    }

    /// <summary>
    /// Fills a block with a deterministic mix of shops, apartments, towers and parking lots. Towers become more
    /// likely and taller towards the north-east, where downtown lies on the map.
    /// </summary>
    private static void FillBlock(Transform parent, Rect block, int seed)
    {
        const float CellWidth = 30f;

        const float CellDepth = 28f;

        const float LotChance = 0.18f;

        const float ShopChance = 0.32f;

        var random = new System.Random(seed);

        var downtown = Mathf.InverseLerp(150f, 330f, block.center.y);

        var columns = Mathf.Max(1, Mathf.RoundToInt(block.width / CellWidth));

        var rows = Mathf.Max(1, Mathf.RoundToInt(block.height / CellDepth));

        var cellWidth = block.width / columns;

        var cellDepth = block.height / rows;

        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var centre = new Vector2(block.xMin + (column + 0.5f) * cellWidth, block.yMin + (row + 0.5f) * cellDepth);

                var facing = NearestEdgeFacing(block, centre);

                var roll = random.NextDouble();

                var towerChance = 0.1f + 0.35f * downtown;

                var name = "Fill" + seed + "_" + column + "_" + row;

                if (roll < towerChance && cellWidth >= 22f)
                {
                    var garage = random.NextDouble() < 0.2;

                    var height = garage ? 14f + (float)random.NextDouble() * 8f : 28f + downtown * 40f + (float)random.NextDouble() * 12f;

                    var style = garage ? BuildingStyle.Garage : random.NextDouble() < 0.5 ? BuildingStyle.Glass : BuildingStyle.Striped;

                    Section(name, parent);

                    BuildBuilding(new BuildingSpec(name, centre, new Vector3(cellWidth - 6f, height, cellDepth - 6f), style, facing));
                }
                else if (roll < towerChance + LotChance)
                {
                    Lot(parent, centre, new Vector2(cellWidth - 2f, cellDepth - 2f), seed + column * 7 + row);
                }
                else if (roll < towerChance + LotChance + ShopChance)
                {
                    Section(name, parent);

                    BuildBuilding(new BuildingSpec(name, centre, new Vector3(cellWidth - 4f, 5f + (float)random.NextDouble() * 3f, cellDepth - 4f), BuildingStyle.Shop, facing));
                }
                else
                {
                    var floors = 4 + random.Next(0, 5);

                    Section(name, parent);

                    BuildBuilding(new BuildingSpec(name, centre, new Vector3(cellWidth - 4f, floors * 3.2f, cellDepth - 4f), BuildingStyle.Apartment, facing));
                }
            }
        }
    }

    /// <summary>Returns the axis direction from <paramref name="centre"/> towards the nearest edge of <paramref name="block"/>.</summary>
    private static Vector3 NearestEdgeFacing(Rect block, Vector2 centre)
    {
        var left = centre.x - block.xMin;

        var right = block.xMax - centre.x;

        var back = centre.y - block.yMin;

        var forward = block.yMax - centre.y;

        var nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(back, forward));

        if (Mathf.Approximately(nearest, left))
        {
            return Vector3.left;
        }

        if (Mathf.Approximately(nearest, right))
        {
            return Vector3.right;
        }

        return Mathf.Approximately(nearest, back) ? Vector3.back : Vector3.forward;
    }

    /// <summary>Adds a row of distant downtown blocks along the new north edge, matching the existing skyline.</summary>
    private static void BuildDistantSkyline()
    {
        Section("DistantSkylineExtra", _district);

        var random = new System.Random(4242);

        for (var x = WorldMinX; x <= WorldMaxX; x += 14f)
        {
            var height = 14f + (float)random.NextDouble() * 34f;

            Make(new Spec("DistantSkylineExtra", new Vector3(x, height / 2f, WorldMaxZ + 12f + (float)random.NextDouble() * 18f), new Vector3(11f, height, 11f), _towerGlass));
        }
    }

    /// <summary>Builds one building according to its style.</summary>
    private static void BuildBuilding(BuildingSpec spec)
    {
        switch (spec.Style)
        {
            case BuildingStyle.Shop:
                BuildShop(spec);

                break;

            case BuildingStyle.Apartment:
                BuildApartment(spec);

                break;

            default:
                BuildTower(spec);

                break;
        }
    }

    /// <summary>Returns the point on the facing wall's surface at the given height.</summary>
    private static Vector3 FacePoint(BuildingSpec spec, float y, float outward)
    {
        var half = Mathf.Abs(spec.Facing.x) > 0.5f ? spec.Size.x / 2f : spec.Size.z / 2f;

        return new Vector3(spec.Centre.x, y, spec.Centre.y) + spec.Facing * (half + outward);
    }

    /// <summary>Returns a scale for a flat strip lying against the facing wall.</summary>
    private static Vector3 FaceScale(BuildingSpec spec, float wallFraction, float height, float thickness)
    {
        return Mathf.Abs(spec.Facing.x) > 0.5f
            ? new Vector3(thickness, height, spec.Size.z * wallFraction)
            : new Vector3(spec.Size.x * wallFraction, height, thickness);
    }

    /// <summary>Builds a low shop with a storefront and awning.</summary>
    private static void BuildShop(BuildingSpec spec)
    {
        var height = spec.Size.y;

        var body = _shopMaterials[_shopIndex % _shopMaterials.Length];

        _shopIndex++;

        Make(new Spec("Body", new Vector3(spec.Centre.x, height / 2f, spec.Centre.y), spec.Size, body, PrimitiveType.Cube, true));

        Make(new Spec("Parapet", new Vector3(spec.Centre.x, height + 0.25f, spec.Centre.y), new Vector3(spec.Size.x + 0.3f, 0.5f, spec.Size.z + 0.3f), _limestone));

        Make(new Spec("Storefront", FacePoint(spec, 2f, 0.06f), FaceScale(spec, 0.8f, 2.4f, 0.12f), _glass));

        var awning = _shopIndex % 2 == 0 ? _red : _amber;

        var awningScale = Mathf.Abs(spec.Facing.x) > 0.5f ? new Vector3(1.8f, 0.15f, spec.Size.z * 0.8f) : new Vector3(spec.Size.x * 0.8f, 0.15f, 1.8f);

        Make(new Spec("Awning", FacePoint(spec, 3.5f, 0.9f), awningScale, awning));

        Make(new Spec("RoofUnit", new Vector3(spec.Centre.x, height + 0.9f, spec.Centre.y), new Vector3(2f, 1f, 2f), _steel));
    }

    /// <summary>Builds a mid-rise apartment block with window strips and balcony slabs on the street face.</summary>
    private static void BuildApartment(BuildingSpec spec)
    {
        const float FloorHeight = 3.2f;

        var height = spec.Size.y;

        var body = _apartmentIndex % 2 == 0 ? _brick : _stucco;

        _apartmentIndex++;

        Make(new Spec("Body", new Vector3(spec.Centre.x, height / 2f, spec.Centre.y), spec.Size, body, PrimitiveType.Cube, true));

        var floors = Mathf.FloorToInt(height / FloorHeight);

        var balconyScale = Mathf.Abs(spec.Facing.x) > 0.5f ? new Vector3(1.4f, 0.22f, spec.Size.z * 0.8f) : new Vector3(spec.Size.x * 0.8f, 0.22f, 1.4f);

        for (var floor = 0; floor < floors; floor++)
        {
            Make(new Spec("Windows", FacePoint(spec, floor * FloorHeight + 1.9f, 0.06f), FaceScale(spec, 0.8f, 1.5f, 0.12f), _glass));

            if (floor > 0)
            {
                Make(new Spec("Balcony", FacePoint(spec, floor * FloorHeight + 0.6f, 0.7f), balconyScale, _concrete));
            }
        }

        Make(new Spec("Parapet", new Vector3(spec.Centre.x, height + 0.3f, spec.Centre.y), new Vector3(spec.Size.x + 0.3f, 0.6f, spec.Size.z + 0.3f), _limestone));

        Make(new Spec("RoofPlant", new Vector3(spec.Centre.x, height + 1.5f, spec.Centre.y), new Vector3(spec.Size.x * 0.35f, 2.4f, spec.Size.z * 0.35f), _steel));
    }

    /// <summary>Builds a podium tower: glass, striped limestone or open parking garage.</summary>
    private static void BuildTower(BuildingSpec spec)
    {
        var x = spec.Centre.x;

        var z = spec.Centre.y;

        var width = spec.Size.x;

        var height = spec.Size.y;

        var depth = spec.Size.z;

        Make(new Spec("Podium", new Vector3(x, PodiumHeight / 2f, z), new Vector3(width + 2f, PodiumHeight, depth + 2f), _limestone, PrimitiveType.Cube, true));

        var bodyMaterial = spec.Style == BuildingStyle.Glass ? _towerGlass : spec.Style == BuildingStyle.Striped ? _limestone : _dark;

        var bodyScale = spec.Style == BuildingStyle.Garage ? new Vector3(width - 0.4f, height, depth - 0.4f) : new Vector3(width, height, depth);

        Make(new Spec("Body", new Vector3(x, PodiumHeight + height / 2f, z), bodyScale, bodyMaterial, PrimitiveType.Cube, true));

        if (spec.Style == BuildingStyle.Glass)
        {
            AddFloorBands(spec, 3.6f, 0.3f, _limestone);
        }
        else if (spec.Style == BuildingStyle.Striped)
        {
            AddFins(spec);
        }
        else
        {
            AddFloorBands(spec, 3f, 0.5f, _concrete);

            AddGarageColumns(spec);
        }

        var top = PodiumHeight + height;

        Make(new Spec("Parapet", new Vector3(x, top + 0.3f, z), new Vector3(width + 0.4f, 0.6f, depth + 0.4f), _limestone));

        Make(new Spec("RoofPlant", new Vector3(x, top + 1.8f, z), new Vector3(width * 0.4f, 3f, depth * 0.4f), _steel));
    }

    /// <summary>Adds horizontal slab bands that read as floors.</summary>
    private static void AddFloorBands(BuildingSpec spec, float spacing, float thickness, Material material)
    {
        for (var y = PodiumHeight + spacing; y < PodiumHeight + spec.Size.y; y += spacing)
        {
            Make(new Spec("Floor", new Vector3(spec.Centre.x, y, spec.Centre.y), new Vector3(spec.Size.x + 0.4f, thickness, spec.Size.z + 0.4f), material));
        }
    }

    /// <summary>Adds the four corner columns of a parking garage.</summary>
    private static void AddGarageColumns(BuildingSpec spec)
    {
        foreach (var sx in new[] { -1f, 1f })
        {
            foreach (var sz in new[] { -1f, 1f })
            {
                var position = new Vector3(spec.Centre.x + sx * spec.Size.x / 2f, PodiumHeight + spec.Size.y / 2f, spec.Centre.y + sz * spec.Size.z / 2f);

                Make(new Spec("Column", position, new Vector3(0.7f, spec.Size.y, 0.7f), _concrete));
            }
        }
    }

    /// <summary>Adds the vertical granite fins that give the striped towers their ribbed look.</summary>
    private static void AddFins(BuildingSpec spec)
    {
        const float Spacing = 2.2f;

        var centreY = PodiumHeight + spec.Size.y / 2f;

        var halfWidth = spec.Size.x / 2f;

        var halfDepth = spec.Size.z / 2f;

        for (var offset = -halfWidth + 1f; offset <= halfWidth - 0.9f; offset += Spacing)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                Make(new Spec("Fin", new Vector3(spec.Centre.x + offset, centreY, spec.Centre.y + side * (halfDepth + 0.15f)), new Vector3(0.6f, spec.Size.y, 0.5f), _granite));
            }
        }

        for (var offset = -halfDepth + 1f; offset <= halfDepth - 0.9f; offset += Spacing)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                Make(new Spec("Fin", new Vector3(spec.Centre.x + side * (halfWidth + 0.15f), centreY, spec.Centre.y + offset), new Vector3(0.5f, spec.Size.y, 0.6f), _granite));
            }
        }
    }

    /// <summary>Creates a striped surface parking lot with parked cars.</summary>
    private static void Lot(Transform parent, Vector2 centre, Vector2 size, int seed)
    {
        Section("Lot", parent);

        Make(new Spec("LotSurface", new Vector3(centre.x, 0.03f, centre.y), new Vector3(size.x, 0.06f, size.y), _asphalt, PrimitiveType.Cube, true));

        var random = new System.Random(seed);

        var rows = size.y >= 28f ? new[] { -1f, 1f } : new[] { 0f };

        foreach (var row in rows)
        {
            var rowZ = centre.y + row * (size.y / 2f - 3.5f);

            for (var x = centre.x - size.x / 2f + 1.4f; x < centre.x + size.x / 2f - 0.7f; x += 2.8f)
            {
                Make(new Spec("StallLine", new Vector3(x - 1.4f, 0.07f, rowZ), new Vector3(0.1f, 0.02f, 5f), _white));

                if (random.NextDouble() < 0.55)
                {
                    Car(new Vector3(x, 0f, rowZ), _carMaterials[random.Next(_carMaterials.Length)]);
                }
            }
        }
    }

    /// <summary>Creates a simple parked car: a body box and a glass cabin.</summary>
    private static void Car(Vector3 position, Material paint)
    {
        Make(new Spec("Car", position + new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.75f, 4.2f), paint, PrimitiveType.Cube, true));

        Make(new Spec("CarCabin", position + new Vector3(0f, 1.3f, -0.1f), new Vector3(1.6f, 0.6f, 2.2f), _glass));
    }

    /// <summary>Creates a park: a lawn, crossing paths and a grid of live oaks.</summary>
    private static void Park(Transform parent, Vector2 centre, Vector2 size)
    {
        Section("Park", parent);

        Make(new Spec("Lawn", new Vector3(centre.x, 0f, centre.y), new Vector3(size.x, 0.08f, size.y), _park));

        Make(new Spec("PathAlongX", new Vector3(centre.x, 0.045f, centre.y), new Vector3(size.x, 0.02f, 2.5f), _concrete));

        Make(new Spec("PathAlongZ", new Vector3(centre.x, 0.046f, centre.y), new Vector3(2.5f, 0.02f, size.y), _concrete));

        for (var x = centre.x - size.x / 2f + 5f; x <= centre.x + size.x / 2f - 4f; x += 14f)
        {
            for (var z = centre.y - size.y / 2f + 6f; z <= centre.y + size.y / 2f - 5f; z += 14f)
            {
                if (Mathf.Abs(x - centre.x) > 3f && Mathf.Abs(z - centre.y) > 3f)
                {
                    Oak(new Vector3(x, 0f, z));
                }
            }
        }
    }

    /// <summary>Builds the elevated freeway deck, barriers, columns and lane paint that run beside the station.</summary>
    private static void BuildFreeway()
    {
        Section("Freeway", _district);

        const float DeckY = 9f;

        const float DeckWidth = 34f;

        const float South = WorldMinZ;

        const float North = WorldMaxZ;

        var length = North - South;

        var centreZ = South + length / 2f;

        Make(new Spec("FreewayDeck", new Vector3(FreewayX, DeckY, centreZ), new Vector3(DeckWidth, 1.2f, length), _concrete, PrimitiveType.Cube, true));

        foreach (var side in new[] { -1f, 1f })
        {
            Make(new Spec("FreewayBarrier", new Vector3(FreewayX + side * (DeckWidth / 2f - 0.4f), DeckY + 1.15f, centreZ), new Vector3(0.8f, 1.1f, length), _concrete, PrimitiveType.Cube, true));
        }

        Make(new Spec("FreewayAsphalt", new Vector3(FreewayX, DeckY + 0.62f, centreZ), new Vector3(DeckWidth - 2f, 0.04f, length), _asphalt));

        foreach (var offset in new[] { -8.5f, 0f, 8.5f })
        {
            for (var z = South + 4f; z < North; z += 9f)
            {
                var material = Mathf.Approximately(offset, 0f) ? _yellow : _white;

                Make(new Spec("FreewayPaint", new Vector3(FreewayX + offset, DeckY + 0.66f, z), new Vector3(0.18f, 0.02f, 4f), material));
            }
        }

        for (var z = South + 8f; z < North; z += 24f)
        {
            foreach (var side in new[] { -1f, 1f })
            {
                Make(new Spec("FreewayColumn", new Vector3(FreewayX + side * 8f, DeckY / 2f - 0.3f, z), new Vector3(2.2f, DeckY - 0.6f, 2.2f), _concrete, PrimitiveType.Cylinder, true));
            }

            Make(new Spec("FreewayCapBeam", new Vector3(FreewayX, DeckY - 0.9f, z), new Vector3(24f, 1.4f, 2.6f), _concrete));
        }
    }

    /// <summary>Creates a live oak: a trunk and three overlapping canopy spheres.</summary>
    private static void Oak(Vector3 position)
    {
        Make(new Spec("OakTrunk", position + Vector3.up * 2f, new Vector3(0.6f, 4f, 0.6f), _wood, PrimitiveType.Cylinder, true));

        Make(new Spec("OakCanopy", position + new Vector3(-1.2f, 4.6f, 0f), Vector3.one * 4.6f, _leaf, PrimitiveType.Sphere));

        Make(new Spec("OakCanopy", position + new Vector3(0f, 5.6f, 0f), Vector3.one * 5f, _leaf, PrimitiveType.Sphere));

        Make(new Spec("OakCanopy", position + new Vector3(1.2f, 4.6f, 0f), Vector3.one * 4.6f, _leaf, PrimitiveType.Sphere));
    }

    /// <summary>Creates a street lamp whose arm reaches along <paramref name="direction"/> (a unit axis).</summary>
    private static void Lamp(Vector3 position, Vector3 direction)
    {
        Make(new Spec("LampPost", position + Vector3.up * 3.5f, new Vector3(0.25f, 7f, 0.25f), _steel, PrimitiveType.Cylinder, true));

        var armScale = new Vector3(Mathf.Abs(direction.x) * 2.2f + 0.15f, 0.15f, Mathf.Abs(direction.z) * 2.2f + 0.15f);

        Make(new Spec("LampArm", position + direction * 1.1f + Vector3.up * 7f, armScale, _steel));

        Make(new Spec("Lamp", position + direction * 2.1f + Vector3.up * 6.9f, new Vector3(0.7f, 0.2f, 0.7f), _amber));
    }

    /// <summary>Returns the spans of a feature range that lie between the crossing road edges.</summary>
    private static List<Vector2> Spans(Vector2 range, Crossing[] crossings)
    {
        var spans = new List<Vector2>();

        var start = range.x;

        foreach (var crossing in crossings)
        {
            var edge = crossing.Centre - crossing.HalfWidth;

            if (edge > start && edge <= range.y)
            {
                spans.Add(new Vector2(start, edge));
            }

            start = Mathf.Max(start, crossing.Centre + crossing.HalfWidth);
        }

        if (range.y > start)
        {
            spans.Add(new Vector2(start, range.y));
        }

        return spans;
    }

    /// <summary>Returns true when <paramref name="along"/> is close enough to a crossing that trees and lamps should be skipped.</summary>
    private static bool NearCrossing(Street street, float along)
    {
        foreach (var crossing in street.Crossings)
        {
            if (Mathf.Abs(along - crossing.Centre) < crossing.HalfWidth + 9f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Converts a (distance along the street, offset across it, height) triple to a world position.</summary>
    private static Vector3 Place(Street street, float along, float across, float y)
    {
        return street.AlongZ ? new Vector3(street.Fixed + across, y, along) : new Vector3(along, y, street.Fixed + across);
    }

    /// <summary>Converts (length along the street, width across it, thickness) to a world scale.</summary>
    private static Vector3 Size(Street street, float along, float across, float thickness)
    {
        return street.AlongZ ? new Vector3(across, thickness, along) : new Vector3(along, thickness, across);
    }

    /// <summary>Creates a named empty group under <paramref name="parent"/> and makes it the current parent.</summary>
    private static void Section(string name, Transform parent)
    {
        var go = new GameObject(name);

        go.transform.SetParent(parent, false);

        _parent = go.transform;
    }

    /// <summary>Creates the primitive described by <paramref name="spec"/> under the current parent.</summary>
    private static GameObject Make(Spec spec)
    {
        var go = GameObject.CreatePrimitive(spec.Shape);

        go.name = spec.Name;

        go.transform.SetParent(_parent, false);

        go.transform.position = spec.Position;

        var scale = spec.Scale;

        if (spec.Shape == PrimitiveType.Cylinder)
        {
            scale = new Vector3(scale.x, scale.y * 0.5f, scale.z);
        }

        go.transform.localScale = scale;

        go.GetComponent<Renderer>().sharedMaterial = spec.Material;

        if (!spec.Collide)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);

        return go;
    }
}
