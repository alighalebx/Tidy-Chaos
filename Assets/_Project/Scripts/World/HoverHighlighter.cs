using UnityEngine;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// Brightens/tints whatever the player is aiming at (drawers, clutter, exit door).
    /// Uses per-material property blocks so no material assets are modified.
    /// </summary>
    public class HoverHighlighter : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private InteractionManager interactionManager = default;
        [SerializeField] private Color tint = new Color(1f, 0.92f, 0.45f, 1f);
        [Range(0f, 1f)]
        [SerializeField] private float strength = 0.4f;
        [SerializeField] private float pulseSpeed = 6f;
        [SerializeField] private bool highlightWhileHolding = false;

        private Renderer[] _current;
        private MaterialPropertyBlock _mpb;

        private void OnEnable()
        {
            _mpb ??= new MaterialPropertyBlock();
            if (interactionManager == null)
            {
                interactionManager = FindFirstObjectByType<InteractionManager>();
            }

            if (interactionManager != null)
            {
                interactionManager.OnHoverStateChanged += HandleHover;
            }
        }

        private void OnDisable()
        {
            if (interactionManager != null)
            {
                interactionManager.OnHoverStateChanged -= HandleHover;
            }

            Clear();
        }

        private void HandleHover(InteractionHoverState state)
        {
            Clear();
            if (state.Target == null || (state.IsHoldingObject && !highlightWhileHolding))
            {
                return;
            }

            _current = state.Target.GetComponentsInChildren<Renderer>();
        }

        private void Update()
        {
            if (_current == null)
            {
                return;
            }

            float k = strength * (0.75f + 0.25f * Mathf.Sin(Time.time * pulseSpeed));
            for (int i = 0; i < _current.Length; i++)
            {
                Renderer r = _current[i];
                if (r == null)
                {
                    continue;
                }

                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null || !mats[m].HasProperty(BaseColorId))
                    {
                        continue;
                    }

                    Color c = mats[m].GetColor(BaseColorId);
                    Color lit = Color.Lerp(c, tint, k) * (1f + k * 0.6f);
                    lit.a = c.a;
                    r.GetPropertyBlock(_mpb, m);
                    _mpb.SetColor(BaseColorId, lit);
                    r.SetPropertyBlock(_mpb, m);
                }
            }
        }

        private void Clear()
        {
            if (_current == null)
            {
                return;
            }

            for (int i = 0; i < _current.Length; i++)
            {
                if (_current[i] == null)
                {
                    continue;
                }

                int count = _current[i].sharedMaterials.Length;
                for (int m = 0; m < count; m++)
                {
                    _current[i].SetPropertyBlock(null, m);
                }
            }

            _current = null;
        }
    }
}
