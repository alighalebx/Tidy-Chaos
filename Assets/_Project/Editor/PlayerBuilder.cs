using UnityEditor;
using UnityEngine;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// Converts Player.prefab into the third-person goofy physics character (pajama bean + camera rig).
    /// Menu: Tidy Chaos/Rebuild Player (TPS Goofy). Safe to re-run.
    /// </summary>
    public static class PlayerBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        private const string MatDir = "Assets/_Project/Art/Materials/";

        [MenuItem("Tidy Chaos/Rebuild Player (TPS Goofy)")]
        public static void Build()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                BuildInto(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[PlayerBuilder] Player prefab rebuilt as third-person goofy bean.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void BuildInto(GameObject root)
        {
            // --- strip first-person bits ---
            var cc = root.GetComponent<CharacterController>();
            if (cc != null) Object.DestroyImmediate(cc);

            Camera cam = root.GetComponentInChildren<Camera>(true);
            Transform oldRig = root.transform.Find("CameraRig");
            Transform rig = oldRig != null ? oldRig : new GameObject("CameraRig").transform;
            rig.SetParent(root.transform, false);
            rig.localPosition = Vector3.zero;
            rig.localRotation = Quaternion.identity;
            if (cam != null)
            {
                cam.transform.SetParent(rig, false);
                cam.transform.localPosition = new Vector3(0f, 1.6f, -3f);
                cam.transform.localRotation = Quaternion.identity;
                cam.nearClipPlane = 0.05f;
            }

            foreach (string n in new[] { "Capsule", "CameraRoot", "CameraRoot ", "HoldPoint", "Visual", "CarryOrigin" })
            {
                Transform t = root.transform.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            // --- physics body ---
            var rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.mass = 60f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var capsule = root.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.3f;
            capsule.height = 1.6f;
            capsule.center = new Vector3(0f, 0.8f, 0f);
            capsule.sharedMaterial = ZeroFriction();

            var carry = new GameObject("CarryOrigin").transform;
            carry.SetParent(root.transform, false);
            carry.localPosition = new Vector3(0f, 1.1f, 0.15f);

            // --- the bean ---
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            GoofyBody body = visual.gameObject.AddComponent<GoofyBody>();

            Material pj = Mat("M_Pajama", new Color(0.2f, 0.42f, 0.9f), 0.15f);
            Material pjDark = Mat("M_PajamaCuff", new Color(0.98f, 0.82f, 0.25f), 0.15f);
            Material skin = Mat("M_Skin", new Color(1f, 0.8f, 0.66f), 0.25f);
            Material slipper = Mat("M_Slipper", new Color(1f, 0.38f, 0.62f), 0.05f);
            Material white = Mat("M_EyeWhite", Color.white, 0.6f);
            Material black = Mat("M_Pupil", new Color(0.05f, 0.05f, 0.08f), 0.8f);
            Material cap = Mat("M_NightCap", new Color(0.9f, 0.15f, 0.2f), 0.1f);
            Material hair = Mat("M_BedHair", new Color(0.35f, 0.22f, 0.12f), 0.1f);

            Transform hips = Node("Hips", visual, Vector3.zero);
            Prim(PrimitiveType.Capsule, "Torso", hips, new Vector3(0f, 0.74f, 0f), Vector3.zero, new Vector3(0.66f, 0.37f, 0.54f), pj);
            for (int i = 0; i < 3; i++)
            {
                Prim(PrimitiveType.Sphere, "Button", hips, new Vector3(0f, 0.95f - i * 0.15f, 0.265f), Vector3.zero, Vector3.one * 0.05f, white);
            }

            Transform head = Node("Head", hips, new Vector3(0f, 1.3f, 0f));
            Prim(PrimitiveType.Sphere, "Skull", head, Vector3.zero, Vector3.zero, new Vector3(0.5f, 0.46f, 0.48f), skin);
            Prim(PrimitiveType.Sphere, "Nose", head, new Vector3(0f, -0.02f, 0.24f), Vector3.zero, new Vector3(0.08f, 0.07f, 0.08f), skin);
            Transform eyeL = Node("EyeL", head, new Vector3(-0.095f, 0.06f, 0.2f));
            Transform eyeR = Node("EyeR", head, new Vector3(0.095f, 0.06f, 0.2f));
            foreach (Transform e in new[] { eyeL, eyeR })
            {
                Prim(PrimitiveType.Sphere, "White", e, Vector3.zero, Vector3.zero, new Vector3(0.13f, 0.15f, 0.08f), white);
                Prim(PrimitiveType.Sphere, "Pupil", e, new Vector3(e == eyeL ? 0.015f : -0.015f, -0.01f, 0.035f), Vector3.zero, new Vector3(0.06f, 0.07f, 0.04f), black);
            }

            // messy bed hair tufts
            Prim(PrimitiveType.Sphere, "Tuft", head, new Vector3(0.17f, 0.12f, 0.02f), new Vector3(0f, 0f, -35f), new Vector3(0.1f, 0.18f, 0.1f), hair);
            Prim(PrimitiveType.Sphere, "Tuft", head, new Vector3(-0.2f, 0.06f, -0.04f), new Vector3(0f, 0f, 50f), new Vector3(0.09f, 0.16f, 0.09f), hair);

            // floppy night cap (stacked shrinking cylinders tipped over) + pompom
            Transform capRoot = Node("NightCap", head, new Vector3(0f, 0.17f, -0.03f));
            capRoot.localRotation = Quaternion.Euler(-25f, 0f, 20f);
            Prim(PrimitiveType.Cylinder, "Brim", capRoot, Vector3.zero, Vector3.zero, new Vector3(0.46f, 0.04f, 0.46f), white);
            float y = 0.05f;
            for (int i = 0; i < 4; i++)
            {
                float s = 0.4f - i * 0.085f;
                Prim(PrimitiveType.Cylinder, "Cap", capRoot, new Vector3(0f, y, -i * 0.035f), new Vector3(-i * 8f, 0f, 0f), new Vector3(s, 0.06f, s), cap);
                y += 0.09f;
            }

            Prim(PrimitiveType.Sphere, "Pompom", capRoot, new Vector3(0f, y + 0.02f, -0.17f), Vector3.zero, Vector3.one * 0.12f, white);

            Transform armL = Limb("ArmL", hips, new Vector3(-0.34f, 1.0f, 0f), 0.24f, 0.13f, pj, skin, pjDark, false);
            Transform armR = Limb("ArmR", hips, new Vector3(0.34f, 1.0f, 0f), 0.24f, 0.13f, pj, skin, pjDark, false);
            Transform legL = Limb("LegL", hips, new Vector3(-0.15f, 0.42f, 0f), 0.2f, 0.17f, pj, slipper, pjDark, true);
            Transform legR = Limb("LegR", hips, new Vector3(0.15f, 0.42f, 0f), 0.2f, 0.17f, pj, slipper, pjDark, true);

            body.hips = hips;
            body.head = head;
            body.armL = armL;
            body.armR = armR;
            body.legL = legL;
            body.legR = legR;
            body.eyes = new[] { eyeL, eyeR };

            // --- components wiring ---
            PlayerController pc = root.GetComponent<PlayerController>();
            ThirdPersonCamera tpc = rig.GetComponent<ThirdPersonCamera>();
            if (tpc == null) tpc = rig.gameObject.AddComponent<ThirdPersonCamera>();
            InteractionManager im = root.GetComponent<InteractionManager>();

            var soCam = new SerializedObject(tpc);
            soCam.FindProperty("target").objectReferenceValue = root.transform;
            soCam.FindProperty("cam").objectReferenceValue = cam;
            soCam.FindProperty("input").objectReferenceValue = root.GetComponent<PlayerInputHandler>();
            soCam.ApplyModifiedPropertiesWithoutUndo();

            var soPc = new SerializedObject(pc);
            soPc.FindProperty("cameraRig").objectReferenceValue = tpc;
            soPc.ApplyModifiedPropertiesWithoutUndo();

            var soBody = new SerializedObject(body);
            soBody.FindProperty("controller").objectReferenceValue = pc;
            soBody.FindProperty("interaction").objectReferenceValue = im;
            soBody.ApplyModifiedPropertiesWithoutUndo();

            if (im != null)
            {
                var soIm = new SerializedObject(im);
                soIm.FindProperty("cameraTransform").objectReferenceValue = cam != null ? cam.transform : null;
                soIm.FindProperty("holdPoint").objectReferenceValue = null;
                soIm.FindProperty("carryOrigin").objectReferenceValue = carry;
                soIm.FindProperty("rayStartsAtPlayerDepth").boolValue = true;
                soIm.FindProperty("interactionRange").floatValue = 2.6f;
                soIm.FindProperty("interactionAssistRadius").floatValue = 0.1f;
                soIm.FindProperty("holdDistance").floatValue = 1.0f;
                soIm.FindProperty("minHoldDistance").floatValue = 0.55f;
                soIm.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Transform Limb(string name, Transform parent, Vector3 pivot, float halfLen, float thick, Material sleeve, Material end, Material cuff, bool isLeg)
        {
            Transform t = Node(name, parent, pivot);
            Prim(PrimitiveType.Capsule, "Segment", t, new Vector3(0f, -halfLen, 0f), Vector3.zero, new Vector3(thick, halfLen, thick), sleeve);
            Prim(PrimitiveType.Cylinder, "Cuff", t, new Vector3(0f, -halfLen * 1.75f, 0f), Vector3.zero, new Vector3(thick * 1.1f, 0.02f, thick * 1.1f), cuff);
            if (isLeg)
            {
                Prim(PrimitiveType.Sphere, "Slipper", t, new Vector3(0f, -halfLen * 2f - 0.02f, 0.07f), Vector3.zero, new Vector3(0.2f, 0.12f, 0.32f), end);
                Prim(PrimitiveType.Sphere, "BunnyEar", t, new Vector3(-0.04f, -halfLen * 2f + 0.07f, 0.17f), new Vector3(-30f, 0f, 10f), new Vector3(0.04f, 0.1f, 0.03f), end);
                Prim(PrimitiveType.Sphere, "BunnyEar", t, new Vector3(0.04f, -halfLen * 2f + 0.07f, 0.17f), new Vector3(-30f, 0f, -10f), new Vector3(0.04f, 0.1f, 0.03f), end);
            }
            else
            {
                Prim(PrimitiveType.Sphere, "Hand", t, new Vector3(0f, -halfLen * 2f - 0.04f, 0f), Vector3.zero, Vector3.one * 0.14f, end);
            }

            return t;
        }

        private static Transform Node(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        private static void Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private static PhysicsMaterial ZeroFriction()
        {
            string path = MatDir + "PM_PlayerZeroFriction.physicMaterial";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm == null)
            {
                pm = new PhysicsMaterial("PM_PlayerZeroFriction");
                AssetDatabase.CreateAsset(pm, path);
            }

            pm.dynamicFriction = 0f;
            pm.staticFriction = 0f;
            pm.bounciness = 0f;
            pm.frictionCombine = PhysicsMaterialCombine.Minimum;
            pm.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        private static Material Mat(string name, Color c, float smooth)
        {
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
