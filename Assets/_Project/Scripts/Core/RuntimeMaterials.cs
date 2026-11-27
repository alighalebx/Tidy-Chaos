using UnityEngine;
using UnityEngine.Rendering;

namespace WheresMyLeftSock.Core
{
    /// <summary>
    /// Shared URP Lit material provider backed by Resources assets so runtime-built primitives
    /// never fall back to Built-in Default-Material or stripped shaders in Standalone builds.
    /// </summary>
    public static class RuntimeMaterials
    {
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessProp = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

        private static Material _litTemplate;
        private static Material _emissiveTemplate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            _litTemplate = null;
            _emissiveTemplate = null;
        }

        /// <summary>Shared URP Lit material safe to assign directly before applying MaterialPropertyBlock tints.</summary>
        public static Material LitShared
        {
            get
            {
                if (_litTemplate == null)
                {
                    _litTemplate = Resources.Load<Material>("RuntimeURPLit");
                    if (_litTemplate == null && GraphicsSettings.currentRenderPipeline != null)
                    {
                        _litTemplate = GraphicsSettings.currentRenderPipeline.defaultMaterial;
                    }
                }

                return _litTemplate;
            }
        }

        private static Material EmissiveShared
        {
            get
            {
                if (_emissiveTemplate == null)
                {
                    _emissiveTemplate = Resources.Load<Material>("RuntimeURPLitEmissive");
                    if (_emissiveTemplate == null)
                    {
                        _emissiveTemplate = LitShared;
                    }
                }

                return _emissiveTemplate;
            }
        }

        /// <summary>Assigns the guaranteed URP Lit material to a runtime primitive Renderer.</summary>
        public static void ApplyLit(Renderer renderer)
        {
            if (renderer == null) return;
            Material shared = LitShared;
            if (shared != null)
            {
                renderer.sharedMaterial = shared;
            }
        }

        /// <summary>Creates a distinct URP Lit material instance with custom color, smoothness, and optional emission.</summary>
        public static Material CreateLitInstance(Color color, float smoothness, bool emissive = false)
        {
            Material template = emissive ? EmissiveShared : LitShared;
            if (template == null) return null;

            Material instance = new Material(template)
            {
                color = color
            };

            if (instance.HasProperty(BaseColorProp))
            {
                instance.SetColor(BaseColorProp, color);
            }

            if (instance.HasProperty(SmoothnessProp))
            {
                instance.SetFloat(SmoothnessProp, emissive ? 0.05f : smoothness);
            }

            if (emissive)
            {
                instance.EnableKeyword("_EMISSION");
                if (instance.HasProperty(EmissionColorProp))
                {
                    instance.SetColor(EmissionColorProp, color * 1.4f);
                }
            }

            return instance;
        }
    }
}
