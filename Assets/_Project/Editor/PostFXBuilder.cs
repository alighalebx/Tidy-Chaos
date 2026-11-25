using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// "September school day" look: warm low sun, soft bloom, creamy highlights, slightly cool shadows,
    /// a touch of grain + vignette. Re-run any time to reset the profile to these values.
    /// </summary>
    public static class PostFXBuilder
    {
        private const string ProfilePath = "Assets/_Project/Art/PostFX/SeptemberMorning.asset";
        private const string VolumeName = "PostFX_SeptemberMorning";

        [MenuItem("Tidy Chaos/Setup Post FX (September Morning)")]
        public static void Build()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // wipe and rebuild so values are deterministic
            foreach (var c in profile.components.ToArray())
            {
                Object.DestroyImmediate(c, true);
            }
            profile.components.Clear();

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            // crisp, clean, slightly cool morning air (NOT golden hour)
            var wb = Add<WhiteBalance>(profile);
            wb.temperature.Override(0f);
            wb.tint.Override(0f);

            var ca = Add<ColorAdjustments>(profile);
            ca.postExposure.Override(0.3f);
            ca.contrast.Override(15f);
            ca.saturation.Override(15f);
            ca.colorFilter.Override(new Color(1f, 0.99f, 0.975f));

            var split = Add<SplitToning>(profile);
            split.shadows.Override(new Color(0.41f, 0.565f, 0.76f));   // soft blue shade
            split.highlights.Override(new Color(1f, 0.91f, 0.78f));   // gently warm sunlight
            split.balance.Override(30f);

            var smh = Add<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(new Vector4(0.955f, 0.99f, 1.07f, 0.01f));
            smh.midtones.Override(new Vector4(1.01f, 1f, 0.985f, 0f));
            smh.highlights.Override(new Vector4(1.025f, 1.01f, 0.97f, 0.025f));

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(0.97f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(new Color(1f, 0.93f, 0.83f));
            bloom.highQualityFiltering.Override(true);

            var vig = Add<Vignette>(profile);
            vig.color.Override(new Color(0.05f, 0.05f, 0.08f));
            vig.intensity.Override(0.16f);
            vig.smoothness.Override(0.5f);
            vig.rounded.Override(false);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(0f);
            grain.response.Override(0.75f);

            var chroma = Add<ChromaticAberration>(profile);
            chroma.intensity.Override(0f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // scene volume
            var go = GameObject.Find(VolumeName);
            if (go == null)
            {
                go = new GameObject(VolumeName);
            }

            var vol = go.GetComponent<Volume>();
            if (vol == null)
            {
                vol = go.AddComponent<Volume>();
            }

            vol.isGlobal = true;
            vol.priority = 10f;
            vol.weight = 1f;
            vol.sharedProfile = profile;

            // light blue morning haze + cool sky fill
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.865f, 0.855f, 0.825f);
            RenderSettings.fogStartDistance = 21f;
            RenderSettings.fogEndDistance = 150f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.7f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.65f, 0.63f, 0.7f);
            RenderSettings.ambientGroundColor = new Color(0.4f, 0.35f, 0.3f);

            // sun: mid-morning, near-white; room lamps: daytime fill, not cosy-evening orange
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(l, "PostFX lights");
                if (l.type == LightType.Directional)
                {
                    Undo.RecordObject(l.transform, "PostFX sun");
                    l.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
                    l.color = new Color(1f, 0.95f, 0.85f);
                    l.intensity = 2.7f;
                }
                else if (l.type == LightType.Point && l.name.StartsWith("Light_"))
                {
                    l.color = new Color(1f, 0.92f, 0.81f);
                    l.intensity = 0.9f;
                }
            }

            // make the editor camera / any scene camera render post too
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                EditorUtility.SetDirty(data);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[PostFX] September morning look applied.");
        }

        private static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }
    }
}
