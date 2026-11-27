using UnityEngine;
using WheresMyLeftSock.Core;

namespace WheresMyLeftSock.Items
{
    /// <summary>
    /// Builds cartoon clothes/keys out of primitives (no imported models needed). Pivot = bottom centre, lies flat.
    /// </summary>
    public static class ItemVisuals
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static MaterialPropertyBlock _mpb;

        public static void Build(Transform parent, ItemData d)
        {
            Color c = d.color;
            Color a = d.accentColor;
            switch (d.shape)
            {
                case ItemShape.Sock:
                case ItemShape.WetSock:
                    Sock(parent, c, a);
                    if (d.shape == ItemShape.WetSock)
                    {
                        Color water = new Color(0.45f, 0.7f, 0.95f);
                        P(parent, PrimitiveType.Cylinder, new Vector3(0.03f, 0.002f, 0f), Vector3.zero, new Vector3(0.36f, 0.002f, 0.4f), water);
                        P(parent, PrimitiveType.Sphere, new Vector3(-0.07f, 0.02f, 0.17f), Vector3.zero, Vector3.one * 0.03f, water);
                        P(parent, PrimitiveType.Sphere, new Vector3(0.14f, 0.015f, -0.13f), Vector3.zero, Vector3.one * 0.025f, water);
                    }

                    break;

                case ItemShape.Pants:
                case ItemShape.ClownPants:
                    Pants(parent, c, a, d.shape == ItemShape.ClownPants);
                    break;

                case ItemShape.Shirt:
                case ItemShape.InsideOutShirt:
                    Shirt(parent, c, a, d.shape == ItemShape.InsideOutShirt);
                    break;

                case ItemShape.Keys:
                    Keys(parent, c, a, 1f);
                    break;

                case ItemShape.ToyKeys:
                    ToyKeys(parent, c, a);
                    break;
            }
        }

        private static void Sock(Transform t, Color c, Color a)
        {
            // L-shaped sock lying flat: leg tube along +z, foot along +x, striped cuff, contrasting heel + toe
            P(t, PrimitiveType.Capsule, new Vector3(0f, 0.045f, 0.06f), new Vector3(90f, 0f, 0f), new Vector3(0.1f, 0.12f, 0.07f), c);
            P(t, PrimitiveType.Capsule, new Vector3(0.07f, 0.045f, -0.06f), new Vector3(0f, 0f, 90f), new Vector3(0.09f, 0.1f, 0.07f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.045f, 0.16f), new Vector3(90f, 0f, 0f), new Vector3(0.105f, 0.012f, 0.075f), a);
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.045f, 0.12f), new Vector3(90f, 0f, 0f), new Vector3(0.105f, 0.008f, 0.075f), a);
            P(t, PrimitiveType.Sphere, new Vector3(0.16f, 0.045f, -0.06f), Vector3.zero, new Vector3(0.08f, 0.08f, 0.075f), a);
            P(t, PrimitiveType.Sphere, new Vector3(-0.005f, 0.045f, -0.07f), Vector3.zero, new Vector3(0.08f, 0.08f, 0.075f), a);
        }

        private static void Pants(Transform t, Color c, Color a, bool clown)
        {
            P(t, PrimitiveType.Cube, new Vector3(0f, 0.035f, 0.13f), Vector3.zero, new Vector3(0.38f, 0.06f, 0.18f), c);
            P(t, PrimitiveType.Cube, new Vector3(0f, 0.04f, 0.23f), Vector3.zero, new Vector3(0.39f, 0.065f, 0.04f), a); // waistband
            P(t, PrimitiveType.Cube, new Vector3(-0.1f, 0.035f, -0.14f), new Vector3(0f, -7f, 0f), new Vector3(0.16f, 0.055f, 0.44f), c);
            P(t, PrimitiveType.Cube, new Vector3(0.1f, 0.035f, -0.14f), new Vector3(0f, 7f, 0f), new Vector3(0.16f, 0.055f, 0.44f), c);
            if (clown)
            {
                // polka dots + giant cuffs + suspenders
                Vector3[] dots =
                {
                    new Vector3(-0.1f, 0.065f, 0.12f), new Vector3(0.11f, 0.065f, 0.09f), new Vector3(-0.12f, 0.065f, -0.08f),
                    new Vector3(0.1f, 0.065f, -0.2f), new Vector3(-0.07f, 0.065f, -0.26f), new Vector3(0.13f, 0.065f, -0.02f),
                };
                foreach (Vector3 p in dots)
                {
                    P(t, PrimitiveType.Sphere, p, Vector3.zero, new Vector3(0.06f, 0.02f, 0.06f), a);
                }

                P(t, PrimitiveType.Cube, new Vector3(-0.13f, 0.04f, -0.37f), new Vector3(0f, -7f, 0f), new Vector3(0.24f, 0.06f, 0.06f), a);
                P(t, PrimitiveType.Cube, new Vector3(0.13f, 0.04f, -0.37f), new Vector3(0f, 7f, 0f), new Vector3(0.24f, 0.06f, 0.06f), a);
                P(t, PrimitiveType.Cube, new Vector3(-0.1f, 0.07f, 0.34f), Vector3.zero, new Vector3(0.03f, 0.01f, 0.22f), new Color(0.2f, 0.5f, 1f));
                P(t, PrimitiveType.Cube, new Vector3(0.1f, 0.07f, 0.34f), Vector3.zero, new Vector3(0.03f, 0.01f, 0.22f), new Color(0.2f, 0.5f, 1f));
            }
            else
            {
                P(t, PrimitiveType.Cube, new Vector3(0f, 0.066f, 0.2f), Vector3.zero, new Vector3(0.04f, 0.01f, 0.03f), new Color(0.85f, 0.7f, 0.3f)); // button
            }
        }

        private static void Shirt(Transform t, Color c, Color a, bool insideOut)
        {
            P(t, PrimitiveType.Cube, new Vector3(0f, 0.025f, 0f), Vector3.zero, new Vector3(0.36f, 0.05f, 0.42f), c);
            P(t, PrimitiveType.Cube, new Vector3(-0.25f, 0.025f, 0.12f), new Vector3(0f, 35f, 0f), new Vector3(0.2f, 0.045f, 0.14f), c);
            P(t, PrimitiveType.Cube, new Vector3(0.25f, 0.025f, 0.12f), new Vector3(0f, -35f, 0f), new Vector3(0.2f, 0.045f, 0.14f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.035f, 0.2f), Vector3.zero, new Vector3(0.16f, 0.025f, 0.08f), a); // collar
            if (insideOut)
            {
                // visible seams + the tag sticking out
                Color seam = new Color(0.25f, 0.25f, 0.25f);
                P(t, PrimitiveType.Cube, new Vector3(-0.178f, 0.052f, 0f), Vector3.zero, new Vector3(0.012f, 0.006f, 0.4f), seam);
                P(t, PrimitiveType.Cube, new Vector3(0.178f, 0.052f, 0f), Vector3.zero, new Vector3(0.012f, 0.006f, 0.4f), seam);
                P(t, PrimitiveType.Cube, new Vector3(0f, 0.052f, -0.205f), Vector3.zero, new Vector3(0.34f, 0.006f, 0.012f), seam);
                P(t, PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.27f), new Vector3(15f, 0f, 0f), new Vector3(0.06f, 0.008f, 0.06f), Color.white);
            }
            else
            {
                P(t, PrimitiveType.Cube, new Vector3(0f, 0.052f, 0.02f), Vector3.zero, new Vector3(0.12f, 0.006f, 0.12f), a); // logo
            }
        }

        private static void Keys(Transform t, Color c, Color a, float s)
        {
            Color hole = new Color(0.1f, 0.1f, 0.1f);
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0f) * s, Vector3.zero, new Vector3(0.1f, 0.006f, 0.1f) * s, c);
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0f) * s, Vector3.zero, new Vector3(0.07f, 0.006f, 0.07f) * s, hole);
            Key(t, new Vector3(0.05f, 0.012f, 0.1f) * s, 20f, c, s);
            Key(t, new Vector3(-0.06f, 0.012f, 0.08f) * s, -30f, c * 0.85f, s);
            P(t, PrimitiveType.Cube, new Vector3(0.02f, 0.014f, -0.09f) * s, new Vector3(0f, 10f, 0f), new Vector3(0.07f, 0.02f, 0.1f) * s, a); // fob
        }

        private static void Key(Transform t, Vector3 pos, float yaw, Color c, float s)
        {
            var k = new GameObject("Key").transform;
            k.SetParent(t, false);
            k.localPosition = pos;
            k.localRotation = Quaternion.Euler(0f, yaw, 0f);
            P(k, PrimitiveType.Cylinder, Vector3.zero, Vector3.zero, new Vector3(0.055f, 0.008f, 0.055f) * s, c);
            P(k, PrimitiveType.Cube, new Vector3(0f, 0f, 0.07f) * s, Vector3.zero, new Vector3(0.02f, 0.012f, 0.1f) * s, c);
            P(k, PrimitiveType.Cube, new Vector3(0.015f, 0f, 0.1f) * s, Vector3.zero, new Vector3(0.015f, 0.012f, 0.015f) * s, c);
            P(k, PrimitiveType.Cube, new Vector3(0.015f, 0f, 0.07f) * s, Vector3.zero, new Vector3(0.015f, 0.012f, 0.012f) * s, c);
        }

        private static void ToyKeys(Transform t, Color c, Color a)
        {
            // chunky baby-toy keys: fat ring + three primary coloured keys
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(0.16f, 0.02f, 0.16f), c);
            P(t, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0f), Vector3.zero, new Vector3(0.1f, 0.02f, 0.1f), new Color(0.1f, 0.1f, 0.1f));
            Color[] cols = { a, new Color(0.2f, 0.5f, 1f), new Color(0.3f, 0.8f, 0.3f) };
            float[] yaws = { -35f, 0f, 35f };
            for (int i = 0; i < 3; i++)
            {
                var k = new GameObject("ToyKey").transform;
                k.SetParent(t, false);
                k.localPosition = new Vector3(0f, 0.025f, 0f);
                k.localRotation = Quaternion.Euler(0f, yaws[i], 0f);
                P(k, PrimitiveType.Cube, new Vector3(0f, 0f, 0.16f), Vector3.zero, new Vector3(0.05f, 0.04f, 0.16f), cols[i]);
                P(k, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.26f), Vector3.zero, new Vector3(0.09f, 0.02f, 0.09f), cols[i]);
            }
        }

        public static GameObject P(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Color color)
        {
            GameObject p = GameObject.CreatePrimitive(type);
            Collider col = p.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localRotation = Quaternion.Euler(euler);
            p.transform.localScale = scale;
            Renderer r = p.GetComponent<Renderer>();
            RuntimeMaterials.ApplyLit(r);
            Tint(r, color);
            return p;
        }

        public static void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, color);
            r.SetPropertyBlock(_mpb);
        }

        public static Bounds LocalBounds(GameObject root)
        {
            bool has = false;
            Bounds b = default;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                Bounds wb = r.bounds;
                Vector3 c = root.transform.InverseTransformPoint(wb.center);
                Vector3 s = root.transform.InverseTransformVector(wb.size);
                var lb = new Bounds(c, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
                if (!has) { b = lb; has = true; }
                else b.Encapsulate(lb);
            }

            return b;
        }
    }
}
