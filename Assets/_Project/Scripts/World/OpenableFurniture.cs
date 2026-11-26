using UnityEngine;

namespace WheresMyLeftSock.World
{
    public enum OpenMode
    {
        Slide = 0,
        Hinge = 1
    }

    /// <summary>
    /// Tap-E drawer / hinged door. Put it on the moving part (needs a collider on it or its children).
    /// </summary>
    public class OpenableFurniture : MonoBehaviour, IInteractable
    {
        [SerializeField] private string label = "Drawer";
        [SerializeField] private OpenMode mode = OpenMode.Slide;
        [Tooltip("Slide: offset in parent-local space when fully open.")]
        [SerializeField] private Vector3 localSlideOffset = new Vector3(0f, 0f, 0.3f);
        [Tooltip("Hinge: rotation axis in this transform's local space.")]
        [SerializeField] private Vector3 localHingeAxis = Vector3.up;
        [SerializeField] private float openAngle = 100f;
        [SerializeField] private float openSpeed = 6f;
        [SerializeField] private bool startOpen = false;
        [SerializeField] private AudioClip openSfx = default;
        [SerializeField] private AudioClip closeSfx = default;

        private Vector3 _closedPos;
        private Quaternion _closedRot;
        private float _t;
        private bool _isOpen;

        public bool IsOpen => _isOpen;
        public OpenMode Mode => mode;
        public string InteractionPrompt => (_isOpen ? "Close " : "Open ") + label;
        public InteractionCategory Category => InteractionCategory.Furniture;
        public bool CanInteract => true;

        public void Configure(string newLabel, OpenMode newMode, Vector3 slideOffset, Vector3 hingeAxis, float angle)
        {
            label = newLabel;
            mode = newMode;
            localSlideOffset = slideOffset;
            localHingeAxis = hingeAxis;
            openAngle = angle;
        }

        private void Awake()
        {
            _closedPos = transform.localPosition;
            _closedRot = transform.localRotation;
            _isOpen = startOpen;
            _t = startOpen ? 1f : 0f;
            Apply();
        }

        public void Interact(GameObject interactor)
        {
            _isOpen = !_isOpen;
            AudioClip clip = _isOpen ? openSfx : closeSfx;
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position);
            }
            else
            {
                WheresMyLeftSock.Core.Sfx.PlayAt(mode == OpenMode.Slide ? "drawer" : "door", transform.position, 0.7f,
                    Random.Range(0.9f, 1.1f) * (_isOpen ? 1f : 0.9f));
            }
        }

        private void Update()
        {
            float target = _isOpen ? 1f : 0f;
            if (Mathf.Approximately(_t, target))
            {
                return;
            }

            _t = Mathf.MoveTowards(_t, target, Time.deltaTime * openSpeed);
            Apply();
        }

        private void Apply()
        {
            float e = 1f - (1f - _t) * (1f - _t); // ease out
            if (mode == OpenMode.Slide)
            {
                transform.localPosition = _closedPos + localSlideOffset * e;
            }
            else
            {
                transform.localRotation = _closedRot * Quaternion.AngleAxis(openAngle * e, localHingeAxis);
            }
        }
    }
}
