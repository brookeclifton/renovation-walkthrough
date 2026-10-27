// Builds the bathroom geometry from the client's drawings.
// Coordinates are inches: x = east from the west wall, z = north from the south wall, y = up from the floor.
// Menu: Tools > Client Bathroom > Rebuild Room Geometry (keeps the XR rig in the scene)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ClientBathroom.EditorTools.MeshKit;

namespace ClientBathroom.EditorTools
{
    public static class BathroomBuilder
    {
        // ---------------------------------------------------------------- Room spec (inches)
        public const float RoomX = 126f;    // north & south walls: 10'6"
        public const float RoomZ = 123f;    // east & west walls: 10'3"
        public const float Ceiling = 96f;   // not on the drawings; assumed 8'-0"
        public const float Tile = 47.24f;   // Boost Pro 120 x 120 cm large format

        // Tub deck / enclosure (west wall) and shower (north-west corner)
        public const float DeckDepth = 53f;      // 24.5" deck + 23.5" cutout + 5" front lip
        public const float DeckLength = 84f;     // 72" to the glass + 12" bench inside the shower
        public const float DeckHeight = 23.5f;   // MTI Andrea 1 undermount is 22-1/4" tall
        public static readonly R TubCutout = new(24.5f, 5f, 48f, 69f);   // 64" x 23.5" (x0, z0, x1, z1)
        public const float GlassZ = 72f;         // tub / shower divider
        public const float ShowerGlassX = 46f;   // shower front glass line
        public const float CurbX0 = 43.75f, CurbX1 = 48.25f, CurbH = 4.5f;

        // Openings
        public static readonly R WestWindow = new(15f, 37f, 69f, 82f);      // (z0, y0, z1, y1)
        public static readonly R NorthWindow = new(49.5f, 43f, 83f, 78f);   // (x0, y0, x1, y1)
        public static readonly R ShowerNiche = new(93f, 59f, 105f, 71f);    // west wall (z, y), 12" wide
        public static readonly R SouthNiche = new(RoomX - 119.5f, 50f, RoomX - 105.5f, 78f);   // south wall (s = 126 - x, y), 14" wide
        public static readonly R ShowerStrip = new(14f, 0f, 28f, Ceiling);  // north wall (x, y): 14" Ivory strip centered on the shower valve
        public const float DoorX0 = 66f, DoorX1 = 98f, DoorH = 80f;         // south wall, hinged on the west jamb
        public const float CreamZ0 = 92f;                                   // east wall cream zone (toilet) z 92..123

        // East wall fixtures
        public const float VanityFrontX = 104.5f;        // all three vanity choices are ~21.5" deep (tops 22")
        public static readonly float[] ElizabethSinkZ = { 16.1f, 55.9f };
        public static readonly float[] TeodorSinkZ = { 14.25f, 57.75f };   // 43.5" sink centers
        public const float ToiletZ = 107.5f;

        public const string Root = "Assets/Bathroom";
        const string GenDir = Root + "/Generated", MatDir = Root + "/Materials", TexDir = Root + "/Textures";

        static Transform _parent;
        static readonly Dictionary<string, Material> Mats = new();

        [MenuItem("Tools/Client Bathroom/Rebuild Room Geometry")]
        public static void BuildMenu() => Build();

        public static GameObject Build()
        {
            EnsureFolders();
            ConfigureTextures();
            CreateMaterials();

            var old = GameObject.Find("Bathroom");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Bathroom");
            _parent = root.transform;

            BuildFloor();
            BuildWalls();
            BuildWindows();
            BuildDeckAndTub();
            BuildShower();
            BuildDoor();
            var vanities = new[] { BuildElizabeth(), BuildTeodor(false), BuildTeodor(true) };
            BuildMirroredCabinet();
            BuildToilet();
            BuildTowelWarmer();
            BuildCeilingAndLights();

            SetupOptions(vanities);
            AssetDatabase.SaveAssets();
            return root;
        }

        // ================================================================ Floor

        static void BuildFloor()
        {
            var group = Group("Floor");
            var holes = new List<R> { new(0, 0, DeckDepth, DeckLength), new(0, DeckLength, CurbX1, RoomZ) };

            // Whole floor is Architessa Level Pebble (12" sheets), continuous with the shower floor
            var floor = new MeshKit();
            foreach (var r in Subtract(new List<R> { new(0, 0, RoomX, RoomZ) }, holes))
                floor.Quad(new(r.x0, PebbleY, r.y0), new(r.x0, PebbleY, r.y1), new(r.x1, PebbleY, r.y1), new(r.x1, PebbleY, r.y0),
                    new(r.x0 / 12f, r.y0 / 12f), new(r.x0 / 12f, r.y1 / 12f), new(r.x1 / 12f, r.y1 / 12f), new(r.x1 / 12f, r.y0 / 12f));
            Emit(floor, "FloorPebble", Mats["Pebble"], group);

            // Walkable area for teleporting: floor minus the deck, shower, vanity and toilet footprints
            var walk = new MeshKit();
            var walkHoles = new List<R>(holes) { new(VanityFrontX - 0.5f, 0, RoomX, 72), new(97f, ToiletZ - 9, RoomX, ToiletZ + 9) };
            foreach (var r in Subtract(new List<R> { new(0, 0, RoomX, RoomZ) }, walkHoles))
                walk.Quad(new(r.x0, 0, r.y0), new(r.x0, 0, r.y1), new(r.x1, 0, r.y1), new(r.x1, 0, r.y0),
                    Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            var walkGo = Emit(walk, "FloorWalkable", null, group, gi: false);
            walkGo.AddComponent<MeshCollider>().sharedMesh = walkGo.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(walkGo.GetComponent<MeshRenderer>());

            // Shower floor: same pebble, its own collider for the second Teleport Interactable
            var peb = new MeshKit();
            peb.Rect(new Vector3(0, PebbleY, DeckLength), Vector3.right * CurbX0, Vector3.forward * (RoomZ - DeckLength), 12f,
                new Vector2(0, DeckLength / 12f));
            var pebGo = Emit(peb, "ShowerFloorPebble", Mats["Pebble"], group);
            var box = pebGo.AddComponent<BoxCollider>();
            box.center = new Vector3(CurbX0 / 2, PebbleY, (DeckLength + RoomZ) / 2) * In;
            box.size = new Vector3(CurbX0, 0.1f, RoomZ - DeckLength) * In;
            var drain = new MeshKit();
            drain.Box(new Vector3(20f, 0.25f, 101.5f), new Vector3(24f, 0.35f, 105.5f), 4f, "-y");
            Emit(drain, "ShowerDrain", Mats["Chrome"], group, probes: true);
        }

        const float PebbleY = 0.25f;

        // ================================================================ Walls

        static void BuildWalls()
        {
            var group = Group("Walls");

            // West wall: tobacco, window over the tub, niche in the shower
            WallTiles("WestWall", group, Vector3.zero, Vector3.forward, RoomZ, "Tobacco",
                new List<R> { WestWindow, ShowerNiche }, 21);

            // North wall: tobacco, window right of the shower; chrome fixtures left of it
            var northOrigin = new Vector3(0, 0, RoomZ);
            var northHoles = new List<R> { NorthWindow };
            northHoles.AddRange(ToiletNiches);
            northHoles.Add(ShowerStrip);
            WallTiles("NorthWall", group, northOrigin, Vector3.right, RoomX, "Tobacco", northHoles, 22);
            var st = new MeshKit(); var sg = new MeshKit();
            TileSurface(st, sg, northOrigin + Vector3.right * ShowerStrip.x0, Vector3.right, Vector3.up,
                ShowerStrip.x1 - ShowerStrip.x0, Ceiling, Tile, Tile, new List<R>(), 27, centerS: false);
            Emit(st, "ShowerIvoryStrip", Mats["TileIvory"], group);
            Emit(sg, "ShowerIvoryStripGrout", Mats["GroutIvory"], group);
            BuildToiletNiches(group, northOrigin);

            // East wall: the strip behind the toilet (north end) and the vanity wall
            var eastOrigin = new Vector3(RoomX, 0, RoomZ);
            var strip = WallTiles("EastWallToiletStrip", group, eastOrigin, Vector3.back, RoomZ - CreamZ0, "Cream", new List<R>(), 23);
            var vanityWall = WallTiles("EastWallVanity", group, eastOrigin + Vector3.back * (RoomZ - CreamZ0), Vector3.back, CreamZ0, "Taupe",
                new List<R>(), 24);

            // South wall: taupe with the door and a niche above the vanity end (s = 126 - x)
            var door = new R(RoomX - DoorX1, 0, RoomX - DoorX0, DoorH);
            var southOrigin = new Vector3(RoomX, 0, 0);
            var south = WallTiles("SouthWall", group, southOrigin, Vector3.left, RoomX, "Taupe", new List<R> { door, SouthNiche }, 25);

            // Niches (3.5" deep)
            var niche = new MeshKit();
            OpeningReturns(niche, Vector3.zero, Vector3.forward, Vector3.up, ShowerNiche, 3.5f, Tile);
            niche.Rect(new Vector3(-3.5f, ShowerNiche.y0, ShowerNiche.x0), Vector3.forward * (ShowerNiche.x1 - ShowerNiche.x0),
                Vector3.up * (ShowerNiche.y1 - ShowerNiche.y0), Tile);
            var showerNiche = Emit(niche, "ShowerNiche", Mats["TileCream"], group).GetComponent<Renderer>();
            var sn = new MeshKit();
            OpeningReturns(sn, southOrigin, Vector3.left, Vector3.up, SouthNiche, 3.5f, Tile);
            sn.Rect(new Vector3(RoomX - SouthNiche.x0, SouthNiche.y0, -3.5f), Vector3.left * (SouthNiche.x1 - SouthNiche.x0),
                Vector3.up * (SouthNiche.y1 - SouthNiche.y0), Tile);
            var southNiche = Emit(sn, "SouthNiche", Mats["TileCream"], group).GetComponent<Renderer>();

            _swapRenderers = new[] { strip.tiles, strip.grout, vanityWall.tiles, vanityWall.grout, south.tiles, south.grout,
                showerNiche, southNiche };
        }

        static Renderer[] _swapRenderers;
        static GameObject _toiletNiches, _toiletNichePatch;

        // Option 2 only: two 24"-wide niches in the north wall beside the toilet (x = s along the north wall):
        // a 10"-tall one at 25"-35" with a post-style toilet paper holder, and a 16"-tall one 4" above it.
        public static readonly R[] ToiletNiches =
        {
            new(88.4f, 25f, 112.4f, 35f), new(88.4f, 39f, 112.4f, 55f),
        };

        static void BuildToiletNiches(Transform walls, Vector3 northOrigin)
        {
            // Tile patch that fills the openings in every other option. Same layout and seed as the north wall,
            // so it lines up seamlessly; it is lightmapped like the wall.
            var patchGroup = Group("ToiletNichePatch");
            patchGroup.SetParent(walls, false);
            var keep = Subtract(new List<R> { new(0, 0, RoomX, Ceiling) }, ToiletNiches);
            var pt = new MeshKit(); var pg = new MeshKit();
            TileSurface(pt, pg, northOrigin, Vector3.right, Vector3.up, RoomX, Ceiling, Tile, Tile, keep, 22);
            Emit(pt, "NorthWallNichePatch", Mats["TileTobacco"], patchGroup);
            Emit(pg, "NorthWallNichePatchGrout", Mats["GroutTobacco"], patchGroup);
            _toiletNichePatch = patchGroup.gameObject;

            // The niches themselves (probe-lit, since they are toggled at runtime)
            var group = Group("ToiletNiches");
            group.SetParent(walls, false);
            const float depth = 3.5f, back = RoomZ + depth;
            var returns = new MeshKit(); var backs = new MeshKit();
            foreach (var n in ToiletNiches)
            {
                OpeningReturns(returns, northOrigin, Vector3.right, Vector3.up, n, depth, Tile);
                backs.Rect(new Vector3(n.x0, n.y0, back), Vector3.right * (n.x1 - n.x0), Vector3.up * (n.y1 - n.y0), Tile);
            }
            Emit(returns, "ToiletNicheReturns", Mats["TileTobacco"], group, gi: false, probes: true);
            Emit(backs, "ToiletNicheBacks", Mats["PebbleWall"], group, gi: false, probes: true);

            // Post-style holder (round rosette, round post, end cap) in matte black, mounted on the niche's LEFT side wall
            // and pointing right; roll centered top to bottom.
            var b0 = ToiletNiches[0];
            float hy = (b0.y0 + b0.y1) / 2, wx = b0.x0, pz = back - 2.3f;
            var tp = new MeshKit();
            tp.Cylinder(new Vector3(wx + 0.15f, hy, pz), Vector3.right, 1.1f, 0.3f, 32);    // rosette
            tp.Cylinder(new Vector3(wx + 3.2f, hy, pz), Vector3.right, 0.38f, 5.8f, 24);    // post
            tp.Cylinder(new Vector3(wx + 6.15f, hy, pz), Vector3.right, 0.7f, 0.25f, 32);   // end cap
            Emit(tp, "ToiletPaperHolder", Mats["MatteBlack"], group, gi: false, probes: true);
            var roll = new MeshKit();
            roll.Cylinder(new Vector3(wx + 3.4f, hy, pz), Vector3.right, 2.25f, 4.5f, 32);
            Emit(roll, "ToiletPaper", Mats["Paint"], group, gi: false, probes: true);

            _toiletNiches = group.gameObject;
        }

        // Tile schemes, per swap renderer: toilet strip tiles/grout, vanity wall tiles/grout, south wall tiles/grout, two niches.
        static readonly string[][] Schemes =
        {
            new[] { "TileCream", "GroutCream", "TileTaupe", "GroutTaupe", "TileTaupe", "GroutTaupe", "TileCream", "TileCream" },     // 1: as designed
            new[] { "TileTobacco", "GroutTobacco", "TileIvory", "GroutIvory", "TileIvory", "GroutIvory", "PebbleWall", "PebbleWall" }, // 2
            new[] { "PebbleWall", "GroutPebble", "TileIvory", "GroutIvory", "TileIvory", "GroutIvory", "PebbleWall", "PebbleWall" },  // 3
        };

        // Options shown in the headset: (label, tile scheme, vanity). 2a/2b are scheme 2 with the Teodor vanities.
        static readonly (string label, int scheme, int vanity, bool toiletNiches)[] OptionList =
        {
            ("Label_Option1", 0, 0, false), ("Label_Option2", 1, 0, true), ("Label_Option2a", 1, 1, false),
            ("Label_Option2b", 1, 2, false), ("Label_Option3", 2, 0, false),
        };

        // Same meshes for every scheme, so the baked lighting stays valid. Vanities are swapped by toggling their groups.
        static void SetupOptions(GameObject[] vanities)
        {
            var sw = _parent.gameObject.AddComponent<TileOptionSwitcher>();
            sw.swapRenderers = _swapRenderers;
            sw.vanities = vanities;
            sw.startOption = 1;   // the client's likely pick
            sw.options = System.Array.ConvertAll(OptionList, o => new TileOptionSwitcher.Option
            {
                label = Tex(o.label),
                materials = System.Array.ConvertAll(Schemes[o.scheme], n => Mats[n]),
                vanity = o.vanity,
                show = new[] { o.toiletNiches ? _toiletNiches : _toiletNichePatch },
            });
            sw.toggled = new[] { _toiletNiches, _toiletNichePatch };

            var label = new MeshKit();   // 0.6 m x 0.15 m card, positioned in front of the viewer at runtime
            label.Quad(new(-11.81f, -2.95f, 0), new(-11.81f, 2.95f, 0), new(11.81f, 2.95f, 0), new(11.81f, -2.95f, 0),
                new(0, 0), new(0, 1), new(1, 1), new(1, 0));
            var labelGo = Emit(label, "OptionLabel", Mats["Label"], _parent, gi: false);
            GameObjectUtility.SetStaticEditorFlags(labelGo, 0);
            var lr = labelGo.GetComponent<MeshRenderer>();
            lr.enabled = false; lr.shadowCastingMode = ShadowCastingMode.Off;
            sw.labelQuad = lr;

            // Only the Elizabeth is active for the lighting bake (all three share the same footprint)
            for (var v = 1; v < vanities.Length; v++) vanities[v].SetActive(false);
            _toiletNiches.SetActive(false);   // the patch is what gets baked
        }

        static (Renderer tiles, Renderer grout) WallTiles(string name, Transform group, Vector3 origin, Vector3 u, float width,
            string color, List<R> holes, int seed)
        {
            var tiles = new MeshKit(); var grout = new MeshKit();
            TileSurface(tiles, grout, origin, u, Vector3.up, width, Ceiling, Tile, Tile, holes, seed);
            return (Emit(tiles, name, Mats["Tile" + color], group).GetComponent<Renderer>(),
                    Emit(grout, name + "Grout", Mats["Grout" + color], group).GetComponent<Renderer>());
        }

        // ================================================================ Windows

        static void BuildWindows()
        {
            var group = Group("Windows");

            // West window over the tub deck: 54" x 45", sill at 37"
            var ret = new MeshKit();
            OpeningReturns(ret, Vector3.zero, Vector3.forward, Vector3.up, WestWindow, 5f, Tile);
            Emit(ret, "WestWindowReturns", Mats["TileTobacco"], group);
            var frame = new MeshKit();
            WindowFrame(frame, new Vector3(-5f, 0, 0), WestWindow, west: true, meetingRail: false);
            Emit(frame, "WestWindowFrame", Mats["TrimWhite"], group);
            var glass = new MeshKit();
            glass.Rect(new Vector3(-4f, WestWindow.y0, WestWindow.x0), Vector3.forward * (WestWindow.x1 - WestWindow.x0),
                Vector3.up * (WestWindow.y1 - WestWindow.y0), 12f);
            Emit(glass, "WestWindowGlass", Mats["WindowGlass"], group, gi: false);
            var sky = new MeshKit();
            sky.Rect(new Vector3(-40f, 10f, -20f), Vector3.forward * 130f, Vector3.up * 100f, 130f);
            Emit(sky, "WestSky", Mats["Sky"], group);
            Blind(group, "WestBlind", new Vector3(-0.3f, WestWindow.y1, WestWindow.x0), Vector3.forward, Vector3.left,
                WestWindow.x1 - WestWindow.x0, WestWindow.y1 - WestWindow.y0);
            AreaLight("WestWindowDaylight", group, new Vector3(-1f, (WestWindow.y0 + WestWindow.y1) / 2, (WestWindow.x0 + WestWindow.x1) / 2),
                Quaternion.LookRotation(Vector3.right), new Vector2(WestWindow.x1 - WestWindow.x0, WestWindow.y1 - WestWindow.y0), 2.2f);

            // North window: double hung, 33.5" x 35"
            var nret = new MeshKit();
            OpeningReturns(nret, new Vector3(0, 0, RoomZ), Vector3.right, Vector3.up, NorthWindow, 5f, Tile);
            Emit(nret, "NorthWindowReturns", Mats["TileTobacco"], group);
            var nframe = new MeshKit();
            WindowFrame(nframe, new Vector3(0, 0, RoomZ + 5f), NorthWindow, west: false, meetingRail: true);
            Emit(nframe, "NorthWindowFrame", Mats["TrimWhite"], group);
            var nglass = new MeshKit();
            nglass.Rect(new Vector3(NorthWindow.x0, NorthWindow.y0, RoomZ + 4f), Vector3.right * (NorthWindow.x1 - NorthWindow.x0),
                Vector3.up * (NorthWindow.y1 - NorthWindow.y0), 12f);
            Emit(nglass, "NorthWindowGlass", Mats["WindowGlass"], group, gi: false);
            var nsky = new MeshKit();
            nsky.Rect(new Vector3(-30f, 10f, RoomZ + 40f), Vector3.right * 180f, Vector3.up * 100f, 180f);
            Emit(nsky, "NorthSky", Mats["Sky"], group);
            Blind(group, "NorthBlind", new Vector3(NorthWindow.x0, NorthWindow.y1, RoomZ + 0.3f), Vector3.right, Vector3.forward,
                NorthWindow.x1 - NorthWindow.x0, NorthWindow.y1 - NorthWindow.y0);
            AreaLight("NorthWindowDaylight", group, new Vector3((NorthWindow.x0 + NorthWindow.x1) / 2, (NorthWindow.y0 + NorthWindow.y1) / 2, RoomZ + 1f),
                Quaternion.LookRotation(Vector3.back), new Vector2(NorthWindow.x1 - NorthWindow.x0, NorthWindow.y1 - NorthWindow.y0), 1.6f);
        }

        // 2" wood blinds, inside mount at the room side of the recess, raised 90%: valance, headrail, stacked slats,
        // bottom rail, black cloth tapes over the stack, and lift cords. Dark walnut stain.
        static void Blind(Transform group, string name, Vector3 frontTopLeft, Vector3 uDir, Vector3 backDir, float width, float drop)
        {
            var wood = new MeshKit(); var tape = new MeshKit();
            Vector3 P(float s, float y, float d) => frontTopLeft + uDir * s + Vector3.up * y + backDir * d;
            void B(MeshKit k, float s0, float y0, float d0, float s1, float y1, float d1)
            {
                Vector3 a = P(s0, y0, d0), b = P(s1, y1, d1);
                k.Box(Vector3.Min(a, b), Vector3.Max(a, b), 12f);
            }
            B(wood, 0, -3f, 0, width, 0, 0.6f);                         // valance
            B(wood, 0.25f, -2f, 0.6f, width - 0.25f, 0, 2.3f);          // headrail
            var slats = Mathf.CeilToInt(drop / 1.8f);
            var y = -3.2f;
            for (var i = 0; i < slats; i++, y -= 0.27f)
                B(wood, 0.3f, y - 0.2f, 0.25f, width - 0.3f, y, 2.25f);
            B(wood, 0.3f, y - 0.6f, 0.2f, width - 0.3f, y, 2.3f);        // bottom rail
            var tapes = width > 40f ? new[] { width * 0.16f, width * 0.5f, width * 0.84f } : new[] { width * 0.2f, width * 0.8f };
            foreach (var t in tapes)
            {
                B(tape, t - 0.75f, y - 0.65f, 0.12f, t + 0.75f, -2f, 0.2f);     // front tape
                B(tape, t - 0.75f, y - 0.65f, 2.3f, t + 0.75f, -2f, 2.38f);     // back tape
            }
            foreach (var c in new[] { width - 2.2f, width - 1.7f })             // lift cords
            {
                var top = P(c, -2f, 1.2f);
                tape.Cylinder(top + Vector3.down * drop * 0.25f, Vector3.up, 0.06f, drop * 0.5f, 8);
                tape.Cylinder(top + Vector3.down * drop * 0.5f, Vector3.up, 0.25f, 1.2f, 12);                   // tassel
            }
            Emit(wood, name + "Wood", Mats["BlindWalnut"], group, probes: true);
            Emit(tape, name + "Tapes", Mats["BlindTape"], group, probes: true);
        }

        // Frame sits at the back of the recess. For the west window, h = (z0, y0, z1, y1); otherwise (x0, y0, x1, y1).
        static void WindowFrame(MeshKit k, Vector3 back, R h, bool west, bool meetingRail)
        {
            const float f = 2f, d = 2f;
            Vector3 Pt(float s, float y, float depth) => west ? new Vector3(back.x + depth, y, s) : new Vector3(s, y, back.z - depth);
            void Bar(float s0, float y0, float s1, float y1)
            {
                var a = Pt(s0, y0, 0); var b = Pt(s1, y1, d);
                k.Box(Vector3.Min(a, b), Vector3.Max(a, b), 12f);
            }
            Bar(h.x0, h.y0, h.x1, h.y0 + f);
            Bar(h.x0, h.y1 - f, h.x1, h.y1);
            Bar(h.x0, h.y0, h.x0 + f, h.y1);
            Bar(h.x1 - f, h.y0, h.x1, h.y1);
            if (meetingRail)
            {
                var m = (h.y0 + h.y1) / 2;
                Bar(h.x0, m - 1f, h.x1, m + 1f);
            }
        }

        // ================================================================ Tub deck, tub

        static void BuildDeckAndTub()
        {
            var group = Group("TubDeck");
            var top = new MeshKit(); var grout = new MeshKit();
            TileSurface(top, grout, new Vector3(0, DeckHeight, 0), Vector3.right, Vector3.forward, DeckDepth, DeckLength, Tile, Tile,
                new List<R> { TubCutout }, 31, centerS: false, centerT: false);
            OpeningReturns(top, new Vector3(0, DeckHeight, 0), Vector3.right, Vector3.forward, TubCutout, 1.5f, Tile);
            Emit(top, "DeckTop", Mats["TileTobacco"], group);
            Emit(grout, "DeckTopGrout", Mats["GroutTobacco"], group);

            var faces = new MeshKit(); var fg = new MeshKit();
            TileSurface(faces, fg, new Vector3(DeckDepth, 0, 0), Vector3.forward, Vector3.up, DeckLength, DeckHeight, Tile, Tile,
                new List<R>(), 32, centerS: false);
            TileSurface(faces, fg, new Vector3(DeckDepth, 0, DeckLength), Vector3.left, Vector3.up, DeckDepth, DeckHeight, Tile, Tile,
                new List<R>(), 33, centerS: false);
            Emit(faces, "DeckFaces", Mats["TileTobacco"], group);
            Emit(fg, "DeckFacesGrout", Mats["GroutTobacco"], group);

            // MTI Andrea 1 basin (undermount, end drain at the south end, sloped backrest to the north)
            var cx = (TubCutout.x0 + TubCutout.x1) / 2;
            var tub = new MeshKit();
            tub.Loft(new[]
            {
                new Ring(2.5f, cx, 35.25f, 10.0f, 26.25f, 4),
                new Ring(3.5f, cx, 35.25f, 10.4f, 26.75f, 4),
                new Ring(12f, cx, 36.25f, 11.0f, 29.75f, 5),
                new Ring(21f, cx, 37.0f, 11.6f, 31.6f, 7),
                new Ring(DeckHeight - 1.5f, cx, 37.0f, 11.75f, 32.0f, 9),
            }, 64, capBottom: true, capTop: false, inward: true);
            Emit(tub, "TubAndrea1", Mats["Dolomatte"], group);

            var fx = new MeshKit();
            fx.Cylinder(new Vector3(cx, 2.55f, 11.5f), Vector3.up, 1.6f, 0.15f);       // drain
            fx.Cylinder(new Vector3(cx, 16f, 6.1f), Vector3.forward, 1.4f, 0.3f);       // overflow
            Emit(fx, "TubDrainOverflow", Mats["Chrome"], group, probes: true);

            // Kohler Parallel wall-mount bath filler (T23491-4-BL), matte black, on the south wall over the tub's
            // south end: spout centered on the tub, a lever handle either side
            const float fy = DeckHeight + 6f;
            var bf = new MeshKit();
            bf.Box(new Vector3(cx - 1.6f, fy - 1.6f, 0), new Vector3(cx + 1.6f, fy + 1.6f, 0.45f), 4f, "-z");          // spout plate
            bf.Box(new Vector3(cx - 1.0f, fy - 0.32f, 0.45f), new Vector3(cx + 1.0f, fy + 0.32f, 9.2f), 4f);           // flat spout
            foreach (var side in new[] { -1f, 1f })
            {
                var hx = cx + side * 6f;
                bf.Box(new Vector3(hx - 1.35f, fy - 1.35f, 0), new Vector3(hx + 1.35f, fy + 1.35f, 0.45f), 4f, "-z");
                bf.Cylinder(new Vector3(hx, fy, 1.05f), Vector3.forward, 0.5f, 1.2f, 20);
                var x0 = side < 0 ? hx - 2.9f : hx; var x1 = side < 0 ? hx : hx + 2.9f;
                bf.Box(new Vector3(x0, fy - 0.38f, 1.4f), new Vector3(x1, fy + 0.38f, 2.3f), 4f);                    // paddle lever
            }
            Emit(bf, "TubFillerParallel", Mats["MatteBlack"], group, probes: true);
        }

        // ================================================================ Shower

        static void BuildShower()
        {
            var group = Group("Shower");
            // Curb (dam) in Level Pebble to match the floor
            var curb = new MeshKit();
            curb.Rect(new Vector3(CurbX0, CurbH, DeckLength), Vector3.right * (CurbX1 - CurbX0), Vector3.forward * (RoomZ - DeckLength), 12f);
            curb.Rect(new Vector3(CurbX1, 0, DeckLength), Vector3.forward * (RoomZ - DeckLength), Vector3.up * CurbH, 12f);
            curb.Rect(new Vector3(CurbX0, 0, RoomZ), Vector3.back * (RoomZ - DeckLength), Vector3.up * CurbH, 12f);
            Emit(curb, "ShowerCurb", Mats["Pebble"], group);

            // 3/8" frameless glass, 80" to the top
            const float t = 0.1875f, top = 80f;
            var g = new MeshKit();
            g.Box(new Vector3(0.1f, DeckHeight, GlassZ - t), new Vector3(ShowerGlassX + t, top, GlassZ + t), 12f);           // divider on the deck
            g.Box(new Vector3(ShowerGlassX - t, DeckHeight, GlassZ + t), new Vector3(ShowerGlassX + t, top, DeckLength), 12f); // over the bench
            g.Box(new Vector3(ShowerGlassX - t, CurbH, DeckLength), new Vector3(ShowerGlassX + t, top, 97f), 12f);             // fixed panel
            g.Box(new Vector3(ShowerGlassX - t, CurbH + 0.25f, 97.25f), new Vector3(ShowerGlassX + t, top, RoomZ - 0.25f), 12f); // door
            Emit(g, "ShowerGlass", Mats["Glass"], group, gi: false);

            var hw = new MeshKit();
            foreach (var y in new[] { 30f, 70f })
            {
                hw.Box(new Vector3(0, y, GlassZ - 0.6f), new Vector3(1.4f, y + 2f, GlassZ + 0.6f), 4f);              // wall clamps
                hw.Box(new Vector3(ShowerGlassX - 0.6f, y - 10f, RoomZ - 1.2f), new Vector3(ShowerGlassX + 0.6f, y - 7f, RoomZ), 4f); // hinges
            }
            foreach (var side in new[] { -1f, 1f })   // door pull, both sides
            {
                var x = ShowerGlassX + side * 1.4f;
                hw.Tube(new[] { new Vector3(x, 37f, 99.75f), new Vector3(x, 51f, 99.75f) }, 0.4f);
                foreach (var y in new[] { 38f, 50f })
                    hw.Tube(new[] { new Vector3(ShowerGlassX, y, 99.75f), new Vector3(x, y, 99.75f) }, 0.25f);
            }
            hw.Box(new Vector3(ShowerGlassX - 0.5f, top - 1.5f, DeckLength - 0.5f), new Vector3(ShowerGlassX + 0.5f, top, DeckLength + 0.5f), 4f);
            Emit(hw, "ShowerGlassHardware", Mats["Chrome"], group, probes: true);

            // Matte black fixtures on the north wall, left of the window
            var fx = new MeshKit();
            const float fxX = 21f;
            fx.Cylinder(new Vector3(fxX, 42f, RoomZ - 0.25f), Vector3.forward, 3.5f, 0.5f);                          // valve trim
            fx.Tube(new[] { new Vector3(fxX, 42f, RoomZ - 0.4f), new Vector3(fxX, 42f, RoomZ - 2f), new Vector3(fxX + 3.5f, 42f, RoomZ - 2.2f) }, 0.35f);
            // Kohler Parallel showerhead (24805-G): round flange, angled arm, square head
            fx.Cylinder(new Vector3(fxX, 80f, RoomZ - 0.2f), Vector3.forward, 1.3f, 0.4f);
            fx.Tube(new[] { new Vector3(fxX, 80f, RoomZ), new Vector3(fxX, 80f, RoomZ - 5f), new Vector3(fxX, 78.2f, RoomZ - 8.5f) }, 0.42f);
            fx.Box(new Vector3(fxX - 3f, 77.4f, RoomZ - 12.5f), new Vector3(fxX + 3f, 78f, RoomZ - 6.5f), 6f);
            // Kohler Shift square handshower (21336) on a wall holder, with hose to a supply elbow
            const float hsX = 25.5f;
            fx.Box(new Vector3(hsX - 0.9f, 51f, RoomZ - 0.5f), new Vector3(hsX + 0.9f, 53f, RoomZ), 4f);                // holder plate
            fx.Box(new Vector3(hsX - 0.7f, 49f, RoomZ - 2.2f), new Vector3(hsX + 0.7f, 60.5f, RoomZ - 0.9f), 4f);       // square handshower
            fx.Cylinder(new Vector3(hsX, 38f, RoomZ - 0.4f), Vector3.forward, 1.1f, 0.5f, 24);                      // supply elbow
            fx.Tube(new[] { new Vector3(hsX, 49f, RoomZ - 1.55f), new Vector3(hsX, 44f, RoomZ - 1.8f), new Vector3(hsX + 1.5f, 33f, RoomZ - 3f),
                new Vector3(hsX + 2.5f, 31f, RoomZ - 3.2f), new Vector3(hsX + 1f, 33f, RoomZ - 2f), new Vector3(hsX, 38f, RoomZ - 0.8f) }, 0.28f);
            Emit(fx, "ShowerFixtures", Mats["MatteBlack"], group, probes: true);
        }

        // ================================================================ Door (south wall)

        static void BuildDoor()
        {
            var group = Group("Door");
            var opening = new R(RoomX - DoorX1, 0, RoomX - DoorX0, DoorH);
            var jamb = new MeshKit();
            OpeningReturns(jamb, new Vector3(RoomX, 0, 0), Vector3.left, Vector3.up, opening, 5f, 24f, skipBottom: true);
            // casing 3.5" x 0.75"
            jamb.Box(new Vector3(DoorX0 - 3.5f, 0, 0), new Vector3(DoorX0, DoorH + 3.5f, 0.75f), 24f, "-z");
            jamb.Box(new Vector3(DoorX1, 0, 0), new Vector3(DoorX1 + 3.5f, DoorH + 3.5f, 0.75f), 24f, "-z");
            jamb.Box(new Vector3(DoorX0 - 3.5f, DoorH, 0), new Vector3(DoorX1 + 3.5f, DoorH + 3.5f, 0.75f), 24f, "-z");
            // six-panel slab, recessed in the opening
            const float face = -1.625f;
            jamb.Box(new Vector3(DoorX0 + 0.1f, 0.5f, -3f), new Vector3(DoorX1 - 0.1f, DoorH - 0.1f, face), 24f);
            foreach (var (u0, u1) in new[] { (6.5f, 15f), (17f, 25.5f) })
            foreach (var (y0, y1) in new[] { (5.5f, 35.5f), (39f, 62f), (66f, 72.5f) })
                jamb.Box(new Vector3(DoorX1 - u1, y0, face), new Vector3(DoorX1 - u0, y1, face + 0.3f), 24f, "-z");
            Emit(jamb, "DoorAndTrim", Mats["TrimWhite"], group);

            // Brass lever (as drawn), latch side to the east
            var lv = new MeshKit();
            const float hx = DoorX1 - 2.75f;
            lv.Cylinder(new Vector3(hx, 36f, face + 0.2f), Vector3.forward, 1.1f, 0.4f);
            lv.Tube(new[] { new Vector3(hx, 36f, face + 0.3f), new Vector3(hx, 36f, face + 2.3f), new Vector3(hx - 4.3f, 36f, face + 2.4f) }, 0.32f);
            lv.Cylinder(new Vector3(hx, 33.4f, face + 0.2f), Vector3.forward, 0.8f, 0.4f);
            Emit(lv, "DoorLever", Mats["Brass"], group, probes: true);
        }

        // ================================================================ Vanities
        // Each vanity group carries its own top, sinks, faucets and LED mirrors (centered on its sinks).
        // Vanity parts are probe-lit so all three look consistent; only the active one is in the lighting bake.

        // Plain white quartz top, square to the tiled wall (no backsplash), with undermount rectangular sinks
        static void VanityTop(Transform group, float[] sinkZ, float ty0, float thick)
        {
            const float tx0 = 104f; var ty1 = ty0 + thick;
            var holes = new List<R>();
            foreach (var sz in sinkZ) holes.Add(new R(107.5f, sz - 8.6f, 119.5f, sz + 8.6f));
            var q = new MeshKit();
            foreach (var r in Subtract(new List<R> { new(tx0, 0, RoomX, 72f) }, holes))
                q.Quad(new(r.x0, ty1, r.y0), new(r.x0, ty1, r.y1), new(r.x1, ty1, r.y1), new(r.x1, ty1, r.y0),
                    Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            foreach (var h in holes) OpeningReturns(q, new Vector3(0, ty1, 0), Vector3.right, Vector3.forward, h, thick, 30f);
            q.Rect(new Vector3(tx0, ty0, 72f), Vector3.back * 72f, Vector3.up * thick, 30f);                          // front edge
            q.Rect(new Vector3(RoomX, ty0, 72f), Vector3.left * (RoomX - tx0), Vector3.up * thick, 30f);              // north end
            q.Quad(new(tx0, ty0, 0), new(RoomX, ty0, 0), new(RoomX, ty0, 72f), new(tx0, ty0, 72f),                    // underside
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            Emit(q, group.name + "_Top", Mats["WhiteQuartz"], group, probes: true);

            // White basins and single-hole chrome faucets (stand-ins; faucets not specified)
            var sink = new MeshKit(); var fct = new MeshKit();
            foreach (var sz in sinkZ)
            {
                sink.Loft(new[]
                {
                    new Ring(ty0 - 4.5f, 113.5f, sz, 5.0f, 7.6f, 4),
                    new Ring(ty0 - 2f, 113.5f, sz, 5.6f, 8.2f, 6),
                    new Ring(ty0 + 0.05f, 113.5f, sz, 6f, 8.6f, 12),
                }, 48, capBottom: true, capTop: false, inward: true);
                fct.Cylinder(new Vector3(113.5f, ty0 - 4.4f, sz), Vector3.up, 0.8f, 0.12f);
                var sy = ty1 + 4.5f;   // spout height on the wall
                fct.Box(new Vector3(RoomX - 0.4f, sy - 1.4f, sz - 1.4f), new Vector3(RoomX, sy + 1.4f, sz + 1.4f), 4f, "+x");   // spout plate
                fct.Box(new Vector3(117.6f, sy - 0.28f, sz - 0.85f), new Vector3(RoomX - 0.4f, sy + 0.28f, sz + 0.85f), 4f);  // flat spout
                foreach (var side in new[] { -1f, 1f })
                {
                    var hz = sz + side * 4.5f;
                    fct.Box(new Vector3(RoomX - 0.4f, sy - 1.25f, hz - 1.25f), new Vector3(RoomX, sy + 1.25f, hz + 1.25f), 4f, "+x");
                    fct.Cylinder(new Vector3(RoomX - 1.0f, sy, hz), Vector3.right, 0.45f, 1.2f, 20);
                    var z0 = side < 0 ? hz - 2.6f : hz; var z1 = side < 0 ? hz : hz + 2.6f;
                    fct.Box(new Vector3(RoomX - 2.1f, sy - 0.35f, z0), new Vector3(RoomX - 1.3f, sy + 0.35f, z1), 4f);         // paddle lever
                }
            }
            Emit(sink, group.name + "_Sinks", Mats["Porcelain"], group, probes: true);
            Emit(fct, group.name + "_Faucets", Mats["MatteBlack"], group, probes: true);

            // 24" x 36" frameless LED mirrors centered on the sinks
            var body = new MeshKit(); var glass = new MeshKit(); var led = new MeshKit();
            var mb = ty1 + 7.5f;   // mirror bottom, clear of the wall-mount faucets
            foreach (var zc in sinkZ)
            {
                body.Box(new Vector3(RoomX - 1.25f, mb, zc - 12f), new Vector3(RoomX, mb + 36f, zc + 12f), 12f, "-x");
                glass.Rect(new Vector3(RoomX - 1.26f, mb, zc + 12f), Vector3.back * 24f, Vector3.up * 36f, 24f);
                foreach (var s in new[] { -8.5f, 7.5f })
                    led.Rect(new Vector3(RoomX - 1.28f, mb + 4f, zc + s + 1f), Vector3.back * 1f, Vector3.up * 28f, 28f);
            }
            Emit(body, group.name + "_MirrorBodies", Mats["Aluminum"], group, gi: false, probes: true);
            Emit(glass, group.name + "_MirrorGlass", Mats["MirrorBox"], group, gi: false);
            Emit(led, group.name + "_MirrorLEDs", Mats["LED"], group, gi: false, probes: true);
        }

        // ---- Willow Elizabeth 72", solid teak (Dark Walnut Teak). Cabinet 71" x 21.5" x 34", 2" top.
        // Front mapped from the cabinet product photo (20 px per inch).

        static Vector2 ElizabethFrontUV(float z, float y) => new((71.5f - z) / 71f, y / 34.15f);

        static void FrontQuad(MeshKit k, float x, float z0, float z1, float y0, float y1)
        {
            k.Quad(new(x, y0, z1), new(x, y1, z1), new(x, y1, z0), new(x, y0, z0),
                ElizabethFrontUV(z1, y0), ElizabethFrontUV(z1, y1), ElizabethFrontUV(z0, y1), ElizabethFrontUV(z0, y0));
        }

        static GameObject BuildElizabeth()
        {
            var group = Group("Vanity_Elizabeth72_Teak");
            const float x0 = VanityFrontX, zA = 0.5f, zB = 71.5f, legH = 5.5f, cabTop = 34f;

            var legsZ = new[] { (69.15f, zB), (41.9f, 43.85f), (28.15f, 30.1f), (zA, 2.85f) };
            var front = new MeshKit();
            FrontQuad(front, x0, zA, zB, legH, cabTop);
            foreach (var (a, b) in legsZ) FrontQuad(front, x0 - 0.02f, a, b, 0, legH);
            Emit(front, "ElizabethFront", Mats["TeakFront"], group, probes: true);

            var body = new MeshKit();
            body.Box(new Vector3(x0 + 0.05f, legH, zA), new Vector3(RoomX, cabTop, zB), 16f, "-x+y");
            foreach (var (a, b) in legsZ)
            {
                body.Box(new Vector3(x0, 0, a), new Vector3(x0 + 2.2f, legH, b), 16f, "-x+y");
                body.Box(new Vector3(RoomX - 2.2f, 0, a), new Vector3(RoomX, legH, b), 16f, "+y");
            }
            // Slatted side panel (north end): frame rails plus vertical slats
            body.Box(new Vector3(x0, legH, zB), new Vector3(RoomX, legH + 2f, zB + 0.4f), 16f, "-z");
            body.Box(new Vector3(x0, cabTop - 2f, zB), new Vector3(RoomX, cabTop, zB + 0.4f), 16f, "-z");
            body.Box(new Vector3(x0, legH + 2f, zB), new Vector3(x0 + 2.35f, cabTop - 2f, zB + 0.4f), 16f, "-z");
            body.Box(new Vector3(RoomX - 2.35f, legH + 2f, zB), new Vector3(RoomX, cabTop - 2f, zB + 0.4f), 16f, "-z");
            for (var x = x0 + 3.2f; x < RoomX - 3.2f; x += 1.3f)
                body.Box(new Vector3(x, legH + 2f, zB), new Vector3(x + 0.75f, cabTop - 2f, zB + 0.35f), 16f, "-z");
            Emit(body, "ElizabethBody", Mats["TeakV"], group, probes: true);

            VanityTop(group, ElizabethSinkZ, cabTop, 2f);
            return group.gameObject;
        }

        // ---- Teodor 72" American Black Walnut, double sink, on tapered walnut legs, no hardware, 3 cm white top.
        // Bridgeport: shaker fronts and shaker sides, cabinet 71" x 33" x 21.5".
        // Malibu: reeded fronts, cabinet 71" x 34" x 21.625".
        // Front layout from the product photos: 2 doors | 3 drawers (16.5") | 2 doors, 1/8" reveals.

        static GameObject BuildTeodor(bool malibu)
        {
            var group = Group(malibu ? "Vanity_Malibu72_Walnut" : "Vanity_Bridgeport72_Walnut");
            var depth = malibu ? 21.625f : 21.5f;
            var cabTop = malibu ? 34f : 33f;
            var legH = malibu ? 6.2f : 5.1f;
            var x0 = RoomX - depth;
            const float zA = 0.5f, zB = 71.5f, gap = 0.0625f, frontT = 0.75f;
            var xf = x0 - frontT;            // fronts are full overlay on the case

            var wood = new MeshKit();
            // Case and plain back/south side
            wood.Box(new Vector3(x0, legH, zA), new Vector3(RoomX, cabTop, zB), 16f, "-x+y");
            // Tapered round legs: four corners plus one at the back center
            foreach (var (lx, lz) in new[] { (x0 + 2.2f, zA + 2.2f), (x0 + 2.2f, zB - 2.2f), (RoomX - 2.2f, zA + 2.2f),
                         (RoomX - 2.2f, zB - 2.2f), (RoomX - 2.2f, 36f) })
                wood.Loft(new[] { new Ring(0, lx, lz, 0.62f, 0.62f), new Ring(legH, lx, lz, 0.95f, 0.95f) }, 20, capBottom: true, capTop: false);

            // Fronts, north to south (z): door, door, drawer column, door, door
            var cols = new[] { (57.525f, 70.8f), (44.25f, 57.525f), (27.75f, 44.25f), (14.475f, 27.75f), (1.2f, 14.475f) };
            var h = cabTop - legH;
            var drawerSplits = new[] { legH, legH + h * 0.39f, legH + h * 0.75f, cabTop };
            for (var idx = 0; idx < cols.Length; idx++)
            {
                var rows = idx == 2 ? drawerSplits : new[] { legH, cabTop };
                for (var r = 0; r < rows.Length - 1; r++)
                {
                    float z0 = cols[idx].Item1 + gap, z1 = cols[idx].Item2 - gap, y0 = rows[r] + gap, y1 = rows[r + 1] - gap;
                    if (malibu) ReededFront(wood, xf, z0, z1, y0, y1, frontT);
                    else ShakerFront(wood, xf, z0, z1, y0, y1, frontT, idx == 2 ? 1.75f : 2.25f);
                }
            }

            // North side, visible from the room: Bridgeport has a shaker side panel, Malibu a plain one
            if (malibu)
                wood.Rect(new Vector3(RoomX, legH, zB), Vector3.left * (RoomX - xf), Vector3.up * h, 16f);
            else
            {
                const float f = 2.5f, t = 0.3f;
                wood.Box(new Vector3(x0, legH, zB), new Vector3(x0 + f, cabTop, zB + t), 16f, "-z");
                wood.Box(new Vector3(RoomX - f, legH, zB), new Vector3(RoomX, cabTop, zB + t), 16f, "-z");
                wood.Box(new Vector3(x0 + f, legH, zB), new Vector3(RoomX - f, legH + f, zB + t), 16f, "-z");
                wood.Box(new Vector3(x0 + f, cabTop - f, zB), new Vector3(RoomX - f, cabTop, zB + t), 16f, "-z");
            }
            Emit(wood, malibu ? "MalibuCabinet" : "BridgeportCabinet", Mats["TeodorWalnut"], group, gi: false, probes: true);

            VanityTop(group, TeodorSinkZ, cabTop, 1.18f);
            return group.gameObject;
        }

        // Shaker door/drawer front facing -x: frame (stiles and rails) around a recessed flat panel
        static void ShakerFront(MeshKit k, float xf, float z0, float z1, float y0, float y1, float t, float frame)
        {
            var f = Mathf.Min(frame, (y1 - y0) / 3.2f);
            const float recess = 0.3f;
            k.Box(new Vector3(xf + recess, y0, z0), new Vector3(xf + t, y1, z1), 16f, "-x");                 // panel
            PanelFace(k, xf + recess, z0 + f, z1 - f, y0 + f, y1 - f);
            k.Box(new Vector3(xf, y0, z0), new Vector3(xf + recess, y1, z0 + f), 16f, "+x");                  // stiles
            k.Box(new Vector3(xf, y0, z1 - f), new Vector3(xf + recess, y1, z1), 16f, "+x");
            k.Box(new Vector3(xf, y0, z0 + f), new Vector3(xf + recess, y0 + f, z1 - f), 16f, "+x");          // rails
            k.Box(new Vector3(xf, y1 - f, z0 + f), new Vector3(xf + recess, y1, z1 - f), 16f, "+x");
        }

        // Front face of a panel facing -x with the walnut figure mapped once across it, like a real veneer panel
        static void PanelFace(MeshKit k, float x, float z0, float z1, float y0, float y1)
        {
            k.Quad(new(x, y0, z1), new(x, y1, z1), new(x, y1, z0), new(x, y0, z0),
                new(0, 0), new(0, 1), new(1, 1), new(1, 0));
        }

        // Reeded (fluted) front facing -x: half-round reeds running vertically, ~5/8" pitch
        static void ReededFront(MeshKit k, float xf, float z0, float z1, float y0, float y1, float t)
        {
            const float depth = 0.16f;
            var back = xf + depth;
            k.Box(new Vector3(back, y0, z0), new Vector3(xf + t, y1, z1), 16f, "-x");
            var n = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / 0.625f));
            var pitch = (z1 - z0) / n;
            const int seg = 6;
            for (var i = 0; i < n; i++)
            for (var j = 0; j < seg; j++)
            {
                float a0 = Mathf.PI * j / seg, a1 = Mathf.PI * (j + 1) / seg;
                float za = z0 + pitch * (i + (1 - Mathf.Cos(a0)) / 2), zb = z0 + pitch * (i + (1 - Mathf.Cos(a1)) / 2);
                float xa = back - depth * Mathf.Sin(a0), xb = back - depth * Mathf.Sin(a1);
                Vector2 UV(float z, float y) => new((z1 - z) / (z1 - z0), (y - y0) / (y1 - y0));   // one veneer figure per front
                // facing -x (towards the room): quad ordered like FrontQuad
                k.Quad(new(xb, y0, zb), new(xb, y1, zb), new(xa, y1, za), new(xa, y0, za),
                    UV(zb, y0), UV(zb, y1), UV(za, y1), UV(za, y0));
            }
            // top and bottom edges of the reed band
            k.Rect(new Vector3(xf, y1, z0), Vector3.right * depth, Vector3.forward * (z1 - z0), 16f);
            k.Rect(new Vector3(xf, y0, z1), Vector3.right * depth, Vector3.back * (z1 - z0), 16f);
        }

        // ================================================================ Mirrors, mirrored cabinet

        static void BuildMirroredCabinet()
        {
            var group = Group("MirroredCabinet");
            // 15" x 72" frameless mirrored cabinet, surface mounted
            var body = new MeshKit(); var glass = new MeshKit();
            body.Box(new Vector3(RoomX - 4.5f, 6f, 74f), new Vector3(RoomX, 78f, 89f), 12f, "-x");
            glass.Rect(new Vector3(RoomX - 4.51f, 6f, 89f), Vector3.back * 15f, Vector3.up * 72f, 72f);
            Emit(body, "CabinetBody", Mats["Aluminum"], group, probes: true);
            Emit(glass, "CabinetMirror", Mats["MirrorBox"], group, gi: false);
        }

        // ================================================================ TOTO toilet (white, one-piece elongated)

        static void BuildToilet()
        {
            var group = Group("Toilet_TOTO");
            float X(float f) => RoomX - f;
            const float z = ToiletZ;
            var k = new MeshKit();
            k.Loft(new[]
            {
                new Ring(0f, X(16f), z, 8.0f, 4.6f, 2.6f),
                new Ring(5f, X(16.3f), z, 8.2f, 4.4f, 2.6f),
                new Ring(10f, X(17f), z, 9.8f, 6.0f, 2.4f),
                new Ring(13.5f, X(17.5f), z, 11.6f, 7.4f, 2.3f),
                new Ring(14.6f, X(17.6f), z, 11.8f, 7.6f, 2.3f),
                new Ring(15.2f, X(17.6f), z, 11.5f, 7.3f, 2.3f),
            }, 56);
            k.Loft(new[]   // closed seat & lid
            {
                new Ring(15.2f, X(17.8f), z, 11.2f, 7.2f, 2.2f),
                new Ring(15.6f, X(17.8f), z, 11.6f, 7.5f, 2.2f),
                new Ring(16.6f, X(17.8f), z, 11.6f, 7.5f, 2.2f),
                new Ring(17.0f, X(17.8f), z, 11.1f, 7.1f, 2.2f),
            }, 56);
            k.Loft(new[]   // tank
            {
                new Ring(13.5f, X(4.75f), z, 4.0f, 7.2f, 5f),
                new Ring(27.0f, X(4.75f), z, 4.1f, 7.8f, 5f),
                new Ring(27.6f, X(4.75f), z, 3.9f, 7.6f, 5f),
                new Ring(28.1f, X(4.75f), z, 3.5f, 7.2f, 5f),
            }, 48);
            Emit(k, "ToiletBody", Mats["Porcelain"], group);
            var lever = new MeshKit();
            lever.Tube(new[] { new Vector3(X(8.8f), 25f, z + 5.5f), new Vector3(X(9.6f), 25f, z + 5.5f), new Vector3(X(9.6f), 24.8f, z + 3f) }, 0.25f);
            Emit(lever, "ToiletLever", Mats["Chrome"], group, probes: true);
        }

        // ================================================================ Eskimo Gordon towel warmer (south wall)

        static void BuildTowelWarmer()
        {
            var group = Group("TowelWarmer_EskimoGordon");
            const float cx = 57f, w = 8.2f, y0 = 34f, h = 39.4f;     // 208 x 1000 mm, clear of the door casing (x 62.5)
            var k = new MeshKit();
            for (var i = 0; i < 3; i++)
            {
                var bx = cx - w / 2 + 0.4f + i * (w - 0.8f - 1.6f) / 2f;
                k.Box(new Vector3(bx, y0 + 1.2f, 1.6f), new Vector3(bx + 1.6f, y0 + h - 1.2f, 3.2f), 12f);
            }
            k.Box(new Vector3(cx - w / 2, y0, 1.4f), new Vector3(cx + w / 2, y0 + 1.2f, 4.3f), 12f);
            k.Box(new Vector3(cx - w / 2, y0 + h - 1.2f, 1.4f), new Vector3(cx + w / 2, y0 + h, 4.3f), 12f);
            foreach (var y in new[] { y0 + 4f, y0 + h - 4f })
                k.Cylinder(new Vector3(cx, y, 0.8f), Vector3.forward, 0.6f, 1.6f, 12);
            Emit(k, "TowelWarmer", Mats["MatteBlack"], group, probes: true);

            // Kohler Parallel Hotelier: 26-1/16" wide (24" mounting centers), 10-3/16" deep, 5-15/16" tall, matte black.
            // Hung level on the south wall over the tub's faucet end, its edge 18" from the towel warmer:
            // two wall plates, square arms straight out from the wall, three rods resting on the top arms, towel bar below.
            const float hw = 26.06f, top = 66f;
            var hc = cx - w / 2 - 18f - hw / 2;
            var ht = new MeshKit();
            foreach (var mx in new[] { hc - 12f, hc + 12f })
            {
                ht.Box(new Vector3(mx - 0.75f, top - 5.94f, 0), new Vector3(mx + 0.75f, top - 0.6f, 0.4f), 4f, "-z");          // wall plate
                ht.Box(new Vector3(mx - 0.3f, top - 1.3f, 0.4f), new Vector3(mx + 0.3f, top - 0.6f, 10.19f), 4f);             // shelf arm
                ht.Box(new Vector3(mx - 0.3f, top - 5.6f, 0.4f), new Vector3(mx + 0.3f, top - 4.9f, 4.4f), 4f);               // bar arm
            }
            foreach (var rz in new[] { 3.6f, 6.6f, 9.6f })
                ht.Tube(new[] { new Vector3(hc - hw / 2, top - 0.3f, rz), new Vector3(hc + hw / 2, top - 0.3f, rz) }, 0.3f);   // shelf rods
            ht.Tube(new[] { new Vector3(hc - hw / 2, top - 5.25f, 3.9f), new Vector3(hc + hw / 2, top - 5.25f, 3.9f) }, 0.32f); // towel bar
            Emit(ht, "KohlerParallelHotelier", Mats["MatteBlack"], group, probes: true);
        }

        // ================================================================ Ceiling, lights, probes

        static void BuildCeilingAndLights()
        {
            var group = Group("CeilingAndLighting");
            var c = new MeshKit();
            c.Rect(new Vector3(0, Ceiling, 0), Vector3.forward * RoomZ, Vector3.right * RoomX, 48f);
            Emit(c, "Ceiling", Mats["Paint"], group);

            var cans = new (float x, float z)[] { (100f, 15.8f), (100f, 56.2f), (75f, 62f), (30f, 37f), (23f, 103f), (110f, 107f) };
            var trim = new MeshKit(); var glow = new MeshKit();
            foreach (var (x, z) in cans)
            {
                trim.Cylinder(new Vector3(x, Ceiling - 0.1f, z), Vector3.up, 2.75f, 0.2f, 32);
                glow.Cylinder(new Vector3(x, Ceiling - 0.25f, z), Vector3.up, 2.0f, 0.1f, 32);
                var go = new GameObject($"CanLight_{x}_{z}");
                go.transform.SetParent(group, false);
                go.transform.localPosition = new Vector3(x, Ceiling - 1f, z) * In;
                go.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.lightmapBakeType = LightmapBakeType.Baked;
                l.spotAngle = 125f; l.innerSpotAngle = 60f; l.range = 4.5f; l.intensity = 2.0f;
                l.color = new Color(1f, 0.92f, 0.83f); l.shadows = LightShadows.Soft; l.shadowRadius = 0.06f;
            }
            Emit(trim, "CanTrims", Mats["Paint"], group, probes: true);
            Emit(glow, "CanLenses", Mats["CanGlow"], group, probes: true);

            var probeGo = new GameObject("RoomReflectionProbe");
            probeGo.transform.SetParent(group, false);
            probeGo.transform.localPosition = new Vector3(RoomX / 2, ProbeHeight, RoomZ / 2) * In;
            var rp = probeGo.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Baked; rp.boxProjection = true; rp.resolution = 256;
            rp.size = new Vector3(RoomX, Ceiling, RoomZ) * In; rp.center = new Vector3(0, (Ceiling / 2 - ProbeHeight) * In, 0);

            var lpg = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            lpg.transform.SetParent(group, false);
            var pts = new List<Vector3>();
            foreach (var x in new[] { 8f, 40f, 70f, 98f, 118f })
            foreach (var z in new[] { 8f, 40f, 75f, 100f, 116f })
            foreach (var y in new[] { 30f, 55f, 85f })
            {
                var inVanity = x > 100f && z < 76f && y < 40f;
                var inDeck = x < 56f && z < 86f && y < DeckHeight + 2f;
                if (!inVanity && !inDeck) pts.Add(new Vector3(x, y, z) * In);
            }
            foreach (var y in new[] { 30f, 47f })   // in front of the toilet niches
                pts.Add(new Vector3(103.4f, y, RoomZ - 4f) * In);
            foreach (var z in new[] { 4f, 20f, 36f, 52f, 68f })
            foreach (var (x, y) in new[] { (100f, 6f), (100f, 22f), (100f, 40f), (113f, 41f), (122f, 50f) })
                pts.Add(new Vector3(x, y, z) * In);
            lpg.probePositions = pts.ToArray();
        }

        static void AreaLight(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos * In; go.transform.localRotation = rot;
            var l = go.AddComponent<Light>();
            l.type = LightType.Rectangle; l.lightmapBakeType = LightmapBakeType.Baked;
            l.areaSize = size * In; l.intensity = intensity; l.color = new Color(0.88f, 0.93f, 1f); l.range = 6f;
        }

        // ================================================================ Assets

        static Transform Group(string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(_parent, false);
            return g;
        }

        static GameObject Emit(MeshKit kit, string name, Material mat, Transform parent, bool gi = true, bool probes = false)
        {
            var mesh = kit.ToMesh(name);
            if (gi && !probes) Unwrapping.GenerateSecondaryUVSet(mesh);
            var path = $"{GenDir}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (gi)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic |
                                                           StaticEditorFlags.BatchingStatic);
                mr.receiveGI = probes ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
            }
            else
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
            return go;
        }

        static void EnsureFolders()
        {
            foreach (var d in new[] { GenDir, MatDir, TexDir })
                if (!AssetDatabase.IsValidFolder(d)) { Directory.CreateDirectory(d); AssetDatabase.Refresh(); }
        }

        static void ConfigureTextures()
        {
            foreach (var path in Directory.GetFiles(TexDir, "*.png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (ti == null) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                var clamp = name.StartsWith("BoostPro") || name.StartsWith("Label_") || name == "Elizabeth_Front";
                ti.alphaIsTransparency = name.StartsWith("Label_");
                ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 2048; ti.anisoLevel = 4; ti.mipmapEnabled = true; ti.sRGBTexture = true;
                ti.SaveAndReimport();
            }
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

        static Material Lit(string name, Color color, float smooth, float metal = 0f, string tex = null)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", tex != null ? Tex(tex) : null);
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static void Emissive(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        static void Transparent(Material m)
        {
            // Clear glass: no environment reflection haze, keep specular highlights
            m.SetFloat("_EnvironmentReflections", 0);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // Box-projected mirror; BathroomXRSetup assigns the baked room cubemap after the lighting bake.
        static void MirrorMaterial()
        {
            const string path = MatDir + "/MirrorBox.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("ClientBathroom/BoxProjectedMirror")); AssetDatabase.CreateAsset(m, path); }
            m.SetVector("_BoxMin", Vector4.zero);
            m.SetVector("_BoxMax", new Vector4(RoomX, Ceiling, RoomZ) * In);
            m.SetVector("_ProbePos", new Vector4(RoomX / 2, ProbeHeight, RoomZ / 2) * In);
            EditorUtility.SetDirty(m);
            Mats["MirrorBox"] = m;
        }

        public const float ProbeHeight = 52f;

        static void LabelMaterial()
        {
            const string path = MatDir + "/Label.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", Tex("Label_Option1"));
            m.SetColor("_BaseColor", Color.white);
            Transparent(m);
            m.renderQueue = (int)RenderQueue.Overlay;
            m.SetFloat("_ZTest", (float)CompareFunction.Always);
            EditorUtility.SetDirty(m);
            Mats["Label"] = m;
        }

        static void CreateMaterials()
        {
            Mats.Clear();
            Lit("TileCream", Color.white, 0.22f, 0, "BoostPro_Cream");
            Lit("TileTaupe", Color.white, 0.22f, 0, "BoostPro_Taupe");
            Lit("TileTobacco", Color.white, 0.22f, 0, "BoostPro_Tobacco");
            Lit("GroutCream", new Color(0.70f, 0.66f, 0.60f), 0.05f);
            Lit("GroutTaupe", new Color(0.44f, 0.41f, 0.37f), 0.05f);
            Lit("GroutTobacco", new Color(0.31f, 0.27f, 0.24f), 0.05f);
            Lit("Pebble", Color.white, 0.35f, 0, "Pebble");
            Lit("Paint", new Color(0.93f, 0.93f, 0.91f), 0.1f);
            Lit("TrimWhite", new Color(0.95f, 0.95f, 0.94f), 0.55f);
            Lit("Chrome", new Color(0.92f, 0.92f, 0.93f), 0.94f, 1f);
            Lit("Brass", new Color(0.80f, 0.62f, 0.36f), 0.72f, 1f);
            Lit("BlindWalnut", new Color(0.55f, 0.45f, 0.4f), 0.35f, 0, "Teodor_WalnutV");
            Lit("BlindTape", new Color(0.03f, 0.03f, 0.03f), 0.05f);
            Lit("MatteBlack", new Color(0.035f, 0.035f, 0.04f), 0.38f, 0.35f);
            Lit("Gunmetal", new Color(0.40f, 0.41f, 0.43f), 0.62f, 0.9f);
            Lit("Aluminum", new Color(0.78f, 0.78f, 0.79f), 0.6f, 1f);
            Lit("Porcelain", new Color(0.96f, 0.96f, 0.95f), 0.86f);
            Lit("Dolomatte", new Color(0.95f, 0.95f, 0.94f), 0.32f);
            Lit("Mirror", new Color(0.96f, 0.96f, 0.96f), 1f, 1f);
            Lit("TeakFront", Color.white, 0.35f, 0, "Elizabeth_Front");
            Lit("TeakV", Color.white, 0.35f, 0, "Elizabeth_TeakV");
            Lit("TeodorWalnut", Color.white, 0.4f, 0, "Teodor_WalnutV");
            Lit("WhiteQuartz", new Color(0.94f, 0.94f, 0.93f), 0.72f);
            Lit("TileIvory", Color.white, 0.22f, 0, "BoostPro_Ivory");
            Lit("GroutIvory", new Color(0.74f, 0.71f, 0.65f), 0.05f);
            Lit("PebbleWall", Color.white, 0.35f, 0, "Pebble").SetTextureScale("_BaseMap", new Vector2(4f, 4f));   // 4 sheets per 120 cm cell
            Lit("GroutPebble", new Color(0.77f, 0.75f, 0.71f), 0.05f);
            LabelMaterial();
            Transparent(Lit("Glass", new Color(0.36f, 0.44f, 0.43f, 0.10f), 0.9f));
            Transparent(Lit("WindowGlass", new Color(0.90f, 0.95f, 1f, 0.05f), 0.9f));
            Emissive(Lit("Sky", Color.black, 0f), new Color(0.55f, 0.68f, 0.86f));
            MirrorMaterial();
            Emissive(Lit("LED", Color.white, 0.5f), new Color(1f, 0.95f, 0.88f) * 2.2f);
            Emissive(Lit("CanGlow", Color.white, 0.5f), new Color(1f, 0.93f, 0.84f) * 3f);
        }
    }
}
