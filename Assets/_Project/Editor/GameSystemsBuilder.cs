using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Items;
using WheresMyLeftSock.Player;
using WheresMyLeftSock.UI;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// Creates the ItemData assets and a "GameSystems" object (spawning, outfit/score, escalation, audio, UI,
    /// wake-up intro), hooks the car + player, removes the old placeholder spawner. Idempotent.
    /// </summary>
    public static class GameSystemsBuilder
    {
        private const string ItemDir = "Assets/_Project/Data/Items";
        private const string ClutterDir = "Assets/_Project/Prefabs/Furniture/Clutter/";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player/Player.prefab";

        [MenuItem("Tidy Chaos/Setup Game Systems")]
        public static void SetupMenu()
        {
            Setup();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[GameSystems] set up.");
        }

        public static void Setup()
        {
            EnsureItems(out List<ItemData> required, out List<ItemData> decoys);

            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.name == "PlaceholderItems_TEST") Object.DestroyImmediate(t.gameObject);
            }

            GameObject sys = GameObject.Find("GameSystems");
            if (sys == null) sys = new GameObject("GameSystems");
            Get<OutfitManager>(sys);
            SpawnManager spawn = Get<SpawnManager>(sys);
            Get<TimerEscalationManager>(sys);
            Get<GameAudio>(sys);
            Get<GameUI>(sys);
            Get<RunIntro>(sys);

            var so = new SerializedObject(spawn);
            SetList(so.FindProperty("requiredItems"), required);
            SetList(so.FindProperty("decoyItems"), decoys);
            var cover = new List<Object>();
            foreach (string n in new[] { "Pillow", "PillowBlue", "PillowLong", "LaundryPile_A", "LaundryPile_B", "LaundryPile_C", "Towel_Blue", "Towel_Pink", "Newspaper" })
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(ClutterDir + n + ".prefab");
                if (p != null) cover.Add(p);
            }

            SetList(so.FindProperty("coverClutter"), cover);
            so.ApplyModifiedPropertiesWithoutUndo();

            var soOutfit = new SerializedObject(sys.GetComponent<OutfitManager>());
            SetList(soOutfit.FindProperty("requiredItems"), required);
            soOutfit.ApplyModifiedPropertiesWithoutUndo();

            GameObject car = GameObject.Find("RideCar");
            if (car != null) Get<RideCar>(car);

            GameObject prefab = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            if (prefab != null)
            {
                if (prefab.GetComponent<OutfitVisuals>() == null)
                {
                    prefab.AddComponent<OutfitVisuals>();
                    PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefab);
                }

                PrefabUtility.UnloadPrefabContents(prefab);
            }

            GameManager gm = Object.FindFirstObjectByType<GameManager>();
            if (gm != null)
            {
                var sog = new SerializedObject(gm);
                sog.FindProperty("roundDurationSeconds").floatValue = 90f;
                sog.FindProperty("startPlayingOnStart").boolValue = false; // RunIntro shows the wake-up title first
                sog.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        private static T Get<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void SetList<T>(SerializedProperty prop, List<T> items) where T : Object
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        // ------------------------------------------------------------------ item definitions (spec section 5)

        private static void EnsureItems(out List<ItemData> required, out List<ItemData> decoys)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data")) AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(ItemDir)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Items");

            Color sock = new Color(1f, 0.82f, 0.2f), sockStripe = new Color(0.9f, 0.2f, 0.25f);
            required = new List<ItemData>
            {
                Item("sock_left", "Left Sock", WearSlot.LeftFoot, ItemQuality.Matching, 20, null, ItemShape.Sock, sock, sockStripe),
                Item("sock_right", "Right Sock", WearSlot.RightFoot, ItemQuality.Matching, 20, null, ItemShape.Sock, sock, sockStripe),
                Item("pants", "Pants", WearSlot.Legs, ItemQuality.Matching, 20, null, ItemShape.Pants, new Color(0.2f, 0.36f, 0.72f), new Color(0.14f, 0.2f, 0.42f)),
                Item("shirt", "Shirt", WearSlot.Torso, ItemQuality.Matching, 20, null, ItemShape.Shirt, new Color(0.96f, 0.96f, 0.93f), new Color(0.9f, 0.25f, 0.25f)),
                Item("keys", "Keys", WearSlot.Accessory, ItemQuality.Matching, 20, null, ItemShape.Keys, new Color(1f, 0.8f, 0.25f), new Color(0.2f, 0.6f, 1f)),
            };

            decoys = new List<ItemData>
            {
                Item("sock_wet", "Soggy Sock", WearSlot.LeftFoot, ItemQuality.Trap, 10, "Squishy Foot", ItemShape.WetSock,
                    new Color(0.55f, 0.65f, 0.45f), new Color(0.38f, 0.44f, 0.32f), Room.Bathroom),
                Item("sock_holey", "Holey Sock", WearSlot.RightFoot, ItemQuality.Trap, 10, "Toe Out", ItemShape.Sock,
                    new Color(0.6f, 0.4f, 0.85f), new Color(0.3f, 0.3f, 0.3f)),
                Item("pants_clown", "Clown Pants", WearSlot.Legs, ItemQuality.Trap, 10, "Circus Commuter", ItemShape.ClownPants,
                    new Color(1f, 0.35f, 0.65f), new Color(1f, 0.9f, 0.2f)),
                Item("shirt_insideout", "Inside-Out Shirt", WearSlot.Torso, ItemQuality.Trap, 10, "Seams Legit", ItemShape.InsideOutShirt,
                    new Color(0.85f, 0.82f, 0.7f), new Color(0.6f, 0.55f, 0.45f)),
                Item("keys_toy", "Toy Keys", WearSlot.Accessory, ItemQuality.Trap, 10, "Toddler Chauffeur", ItemShape.ToyKeys,
                    new Color(0.9f, 0.2f, 0.2f), new Color(1f, 0.85f, 0.1f)),
            };

            AssetDatabase.SaveAssets();
        }

        private static ItemData Item(string id, string name, WearSlot slot, ItemQuality q, int points, string tag, ItemShape shape,
            Color color, Color accent, params Room[] rooms)
        {
            string path = $"{ItemDir}/Item_{id}.asset";
            var d = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (d != null) return d; // keep designer tweaks once created
            d = ScriptableObject.CreateInstance<ItemData>();
            d.itemId = id;
            d.displayName = name;
            d.slot = slot;
            d.quality = q;
            d.dignityPoints = points;
            d.funnyTag = tag;
            d.shape = shape;
            d.color = color;
            d.accentColor = accent;
            d.allowedRooms = rooms ?? new Room[0];
            d.pickupSfx = "pop";
            AssetDatabase.CreateAsset(d, path);
            return d;
        }
    }
}
