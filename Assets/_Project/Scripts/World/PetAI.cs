using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Items;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.World
{
    /// <summary>
    /// Chaos Hazard #2 (spec stretch): runtime-spawned sock-stealing ginger cat that patrols the house using
    /// a runtime HidingSpot-derived walkable graph (zero NavMesh bake or scene edits required).
    /// Handles dynamic hinged doors & floor clutter cleanly, uses smart room-looping flee scoring,
    /// taunts from afar, telegraphs "zoomie jukes" up close, and drops its sock in a pop-up arc on thrown-prop bonks or E/LMB grabs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PetAI : MonoBehaviour, IInteractable
    {
        private enum PetState
        {
            Patrol,
            Stalking,
            FleeWithSock,
            HoldingSock,
            Taunt,
            JukeTelegraph,
            JukeDash,
            ScaredFlee
        }

        private struct NavNode
        {
            public Vector3 position;
            public Room room;
            public bool hasRoom;
        }

        private struct DustPuff
        {
            public Transform tr;
            public float age;
            public float maxAge;
            public Vector3 vel;
        }

        [Header("Behavior Timing & Speeds")]
        [SerializeField] private float graceDelay = 12f;
        [SerializeField] private float moveSpeed = 2.8f;
        [SerializeField] private float fleeSpeed = 4.6f;
        [SerializeField] private float stealCooldown = 22f;
        [SerializeField] private float pickupDistance = 1.25f;
        [SerializeField] private float roomSampleStep = 0.85f;

        private const float JukeSpeed = 6.8f;
        private const float JukeTelegraphDuration = 0.20f;
        private const float JukeDashDuration = 0.55f;
        private const float JukeRecoveryDuration = 0.60f;
        private const float JukeRecoverySpeedMult = 0.62f;

        private Rigidbody _rb;
        private CapsuleCollider _col;
        private PhysicsMaterial _slideMat;
        private readonly List<Material> _runtimeMaterials = new List<Material>();
        private Material _dustMat;

        // Procedural visual hierarchy
        private Transform _visualRoot;
        private Transform _bodyPivot;
        private Transform _headPivot;
        private Transform _mouthAnchor;
        private Transform _tailPivot;
        private Transform _eyeL;
        private Transform _eyeR;
        private Transform[] _legs;
        private GameObject _alertMarker;

        // Runtime dust puffs (no extra MonoBehaviour needed)
        private readonly List<DustPuff> _dustPuffs = new List<DustPuff>();
        private float _nextDustTime;

        // Runtime navigation graph (derived from static floor + HidingSpot metadata)
        private readonly List<NavNode> _nodes = new List<NavNode>();
        private readonly List<List<int>> _adj = new List<List<int>>();
        private readonly List<Vector3> _path = new List<Vector3>();
        private readonly Collider[] _overlapBuf = new Collider[32];
        private int _pathIndex;
        private int _goalNode = -1;
        private float _floorY = 0.105f;
        private Collider _floorCollider;

        // State machine & timers
        private PetState _state = PetState.Patrol;
        private WearableItem _targetSock;
        private WearableItem _carriedSock;
        private PlayerController _player;
        private float _stealReadyTime;
        private float _scaredEndTime;
        private float _nextTargetScanTime;
        private float _nextFleeReevalTime;
        private float _nextStepSfxTime;
        private float _nextVocalTime;
        private float _nextBlinkTime;
        private float _blinkEndTime;
        private float _patrolWaitTimer;
        private float _pawWaitEndTime;
        private float _nextClutterSwipeTime;

        // Taunt & Juke timers
        private float _tauntEndTime;
        private float _nextTauntReadyTime;
        private float _jukePhaseEndTime;
        private float _nextJukeReadyTime;
        private float _jukeRecoveryEndTime;
        private Vector3 _jukeDir;

        // Bonk, clutter, & unstuck timers
        private readonly Dictionary<int, float> _nudgedIgnoreUntil = new Dictionary<int, float>();
        private float _bonkCooldownEnd;
        private float _bonkSpinTimer;
        private float _nextClutterHopTime;
        private float _stuckTimer;
        private float _hardStuckTimer;
        private bool _softHopTriggered;
        private int _stalkStuckCount;
        private Vector3 _stuckSamplePos;
        private Vector3 _desiredMoveDir;
        private float _desiredSpeed;
        private float _queuedHopVel;

        // Read-only diagnostic telemetry
        private string _componentSizesCsv = string.Empty;

        public int RawNodeCount { get; private set; }
        public int PrunedNodeCount => _nodes.Count;
        public int SoftStuckCount { get; private set; }
        public int HardStuckCount { get; private set; }
        public int StealToastCount { get; private set; }
        public string CurrentStateName => _state.ToString();
        public bool IsCarryingSock => _carriedSock != null;
        public string CarriedSockName => _carriedSock != null ? _carriedSock.InteractionPrompt : string.Empty;
        public string TargetSockName => _targetSock != null ? _targetSock.InteractionPrompt : string.Empty;

        public int ConnectedRoomCount
        {
            get
            {
                HashSet<Room> rooms = new HashSet<Room>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].hasRoom) rooms.Add(_nodes[i].room);
                }
                return rooms.Count;
            }
        }

        public string ConnectedRoomsCsv
        {
            get
            {
                HashSet<Room> rooms = new HashSet<Room>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].hasRoom) rooms.Add(_nodes[i].room);
                }
                List<string> names = new List<string>();
                foreach (Room r in rooms) names.Add(r.ToString());
                names.Sort();
                return string.Join(", ", names);
            }
        }

        public bool HasConnectedRoom(string roomName)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].hasRoom && _nodes[i].room.ToString() == roomName) return true;
            }
            return false;
        }

        public string InteractionPrompt => _carriedSock != null
            ? $"Grab {_carriedSock.InteractionPrompt} back"
            : "Pet Cat";

        public InteractionCategory Category => _carriedSock != null
            ? InteractionCategory.Collectible
            : InteractionCategory.None;

        public bool CanInteract => _carriedSock != null &&
                                   (GameManager.Instance == null || GameManager.Instance.CurrentState == GameState.Playing);

        /// <summary>Applies SpawnManager Inspector tuning before the first simulation step.</summary>
        public void Configure(float grace, float walkSpeed, float escapeSpeed, float cooldown)
        {
            graceDelay = Mathf.Max(2f, grace);
            moveSpeed = Mathf.Max(0.8f, walkSpeed);
            fleeSpeed = Mathf.Max(moveSpeed, escapeSpeed);
            stealCooldown = Mathf.Max(3f, cooldown);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<CapsuleCollider>();

            _slideMat = new PhysicsMaterial("PetZeroFriction")
            {
                staticFriction = 0f,
                dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            // Upright capsule avoids horizontal capsule edge-snagging on doorways & furniture
            _col.direction = 1;
            _col.sharedMaterial = _slideMat;
            UpdateColliderForCarryState(false);

            _rb.mass = 8f;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 4f;
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            BuildVisual();
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            IgnoreHingedDoorCollisions();
            BuildNavGraph();
            SnapToSafeSpawnNode();
            _bonkCooldownEnd = Time.time + 1.5f;
            _stealReadyTime = Time.time + graceDelay;
            _nextBlinkTime = Time.time + Random.Range(2f, 5f);
            _stuckSamplePos = transform.position;
            PickPatrolPath();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _dustPuffs.Count; i++)
            {
                if (_dustPuffs[i].tr != null) Destroy(_dustPuffs[i].tr.gameObject);
            }
            _dustPuffs.Clear();

            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                if (_runtimeMaterials[i] != null) Destroy(_runtimeMaterials[i]);
            }
            _runtimeMaterials.Clear();

            if (_slideMat != null) Destroy(_slideMat);
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract) return;
            WearableItem sock = _carriedSock;
            _carriedSock = null;
            _targetSock = null;
            UpdateColliderForCarryState(false);
            if (_alertMarker != null) _alertMarker.SetActive(false);

            sock.ReclaimFromPet(interactor);
            Sfx.PlayAt("hiss", transform.position, 0.75f, Random.Range(0.95f, 1.1f));
            TimerEscalationManager.Toast("YOINK! Reclaimed your sock from the cat!");
            StartScaredFlee();
        }

        private void OnCollisionEnter(Collision collision)
        {
            HandlePropCollision(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            HandlePropCollision(collision);
        }

        private void HandlePropCollision(Collision collision)
        {
            if (GameManager.Instance != null &&
                (GameManager.Instance.CurrentState != GameState.Playing || GameManager.Instance.IsPaused))
            {
                return;
            }

            PhysicsClutter clutter = collision.collider != null
                ? collision.collider.GetComponentInParent<PhysicsClutter>()
                : null;
            if (clutter == null || clutter.IsCurrentlyHeld) return;

            // Only props thrown toward the cat (real world speed, not cat walking into stationary clutter) trigger a bonk.
            if (IsFastThrownTowardCat(clutter))
            {
                HandleBonkedByClutter(clutter.transform.position);
                return;
            }

            // Stationary or slow floor clutter: nudge aside & hop over low clutter instead of getting trapped
            if (clutter.Body != null && !clutter.Body.isKinematic)
            {
                _nudgedIgnoreUntil[clutter.GetInstanceID()] = Time.time + 0.8f;
                Vector3 push = clutter.transform.position - transform.position;
                push.y = 0f;
                if (push.sqrMagnitude < 0.001f) push = transform.forward;
                float impulse = Mathf.Clamp(1.4f / Mathf.Max(0.4f, clutter.Body.mass), 0.25f, 1.1f);
                clutter.Body.AddForce(push.normalized * impulse, ForceMode.VelocityChange);
            }

            if (collision.collider.bounds.max.y <= transform.position.y + 0.45f && Time.time >= _nextClutterHopTime)
            {
                _nextClutterHopTime = Time.time + 0.55f;
                _queuedHopVel = Mathf.Max(_queuedHopVel, 3.6f);
            }
        }

        private void Update()
        {
            TickDustPuffs();

            if (GameManager.Instance != null)
            {
                if (GameManager.Instance.CurrentState != GameState.Playing)
                {
                    _desiredMoveDir = Vector3.zero;
                    _desiredSpeed = 0f;
                    if (_alertMarker != null && _alertMarker.activeSelf) _alertMarker.SetActive(false);
                    return;
                }
                if (GameManager.Instance.IsPaused) return;
            }

            TickStateMachine();
            AnimateBody();
        }

        private void FixedUpdate()
        {
            if (GameManager.Instance != null &&
                (GameManager.Instance.CurrentState != GameState.Playing || GameManager.Instance.IsPaused))
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                return;
            }

            CheckProximityThrownClutter();

            Vector3 vel = _rb.linearVelocity;
            if (_queuedHopVel > 0f)
            {
                vel.y = Mathf.Max(vel.y, _queuedHopVel);
                _queuedHopVel = 0f;
            }

            float speed = _desiredSpeed;
            if (_carriedSock != null && Time.time < _jukeRecoveryEndTime && _state != PetState.JukeDash)
            {
                speed *= JukeRecoverySpeedMult;
            }

            Vector3 steer = _state == PetState.Taunt || _state == PetState.JukeTelegraph
                ? Vector3.zero
                : ApplyWallAvoidance(_desiredMoveDir);

            Vector3 targetXZ = steer * speed;
            float accel = _state == PetState.JukeDash ? 18f : 11f;
            vel.x = Mathf.Lerp(vel.x, targetXZ.x, Time.fixedDeltaTime * accel);
            vel.z = Mathf.Lerp(vel.z, targetXZ.z, Time.fixedDeltaTime * accel);
            _rb.linearVelocity = vel;
            _rb.angularVelocity = Vector3.zero;

            if (_state == PetState.Taunt && _player != null)
            {
                Vector3 toPlayer = _player.transform.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f)
                {
                    Quaternion lookPlayer = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                    _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, lookPlayer, Time.fixedDeltaTime * 9f));
                }
            }
            else if (steer.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(steer, Vector3.up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, Time.fixedDeltaTime * 13f));
            }
        }

        // ───────────────────────────── State Machine ─────────────────────────────

        private void TickStateMachine()
        {
            switch (_state)
            {
                case PetState.Patrol:
                    TickPatrol();
                    break;
                case PetState.Stalking:
                    TickStalking();
                    break;
                case PetState.FleeWithSock:
                    TickFleeWithSock();
                    break;
                case PetState.HoldingSock:
                    TickHoldingSock();
                    break;
                case PetState.Taunt:
                    TickTaunt();
                    break;
                case PetState.JukeTelegraph:
                    TickJukeTelegraph();
                    break;
                case PetState.JukeDash:
                    TickJukeDash();
                    break;
                case PetState.ScaredFlee:
                    TickScaredFlee();
                    break;
            }

            if (_desiredSpeed > 0.5f && Time.time >= _nextStepSfxTime)
            {
                bool fast = _state == PetState.FleeWithSock || _state == PetState.ScaredFlee || _state == PetState.JukeDash;
                _nextStepSfxTime = Time.time + (_state == PetState.JukeDash ? 0.14f : (fast ? 0.22f : 0.36f));
                Sfx.PlayAt("step", transform.position, _state == PetState.JukeDash ? 0.22f : 0.14f, Random.Range(1.45f, 1.85f));
            }

            if (_carriedSock != null && _desiredSpeed > 3.2f && Time.time >= _nextDustTime)
            {
                _nextDustTime = Time.time + (_state == PetState.JukeDash ? 0.07f : 0.17f);
                SpawnDustPuff(transform.position + Vector3.up * 0.08f, -transform.forward * 0.4f + Vector3.up * 0.4f);
            }

            if (_desiredSpeed > 0.35f && _state != PetState.Taunt && _state != PetState.JukeTelegraph)
            {
                TickUnstuckRecovery();
            }
            else
            {
                ResetStuckTracking();
            }
        }

        private void TickPatrol()
        {
            if (Time.time >= _stealReadyTime && Time.time >= _nextTargetScanTime)
            {
                _nextTargetScanTime = Time.time + 1.0f;
                WearableItem candidate = FindTargetSock();
                if (candidate != null)
                {
                    _targetSock = candidate;
                    _stalkStuckCount = 0;
                    _state = PetState.Stalking;
                    RebuildPathTo(_targetSock.transform.position);
                    ResetStuckTracking();
                    return;
                }
            }

            if (_patrolWaitTimer > 0f)
            {
                _patrolWaitTimer -= Time.deltaTime;
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
                if (_patrolWaitTimer <= 0f) PickPatrolPath();
                return;
            }

            if (FollowPath(moveSpeed * 0.85f, 0.45f))
            {
                _patrolWaitTimer = Random.Range(0.55f, 1.4f);
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
            }
        }

        private void TickStalking()
        {
            if (Time.time < _pawWaitEndTime)
            {
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
                return;
            }

            if (!IsSockStealable(_targetSock))
            {
                _targetSock = null;
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            Vector3 sockPos = _targetSock.transform.position;
            float xzDist = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(sockPos.x, sockPos.z));

            OpenableFurniture blocker = GetBlockingFurniture(_targetSock);
            if (blocker != null)
            {
                float doorXZ = Vector2.Distance(
                    new Vector2(transform.position.x, transform.position.z),
                    new Vector2(blocker.transform.position.x, blocker.transform.position.z));

                if (xzDist <= 1.75f || doorXZ <= 1.65f)
                {
                    blocker.Interact(gameObject);
                    _queuedHopVel = Mathf.Max(_queuedHopVel, 3.0f);
                    PlayVocal("meow", 0.72f, Random.Range(1.08f, 1.24f));
                    _pawWaitEndTime = Time.time + 0.45f;
                    _desiredMoveDir = Vector3.zero;
                    _desiredSpeed = 0f;
                    ResetStuckTracking();
                    return;
                }
            }
            else
            {
                if (xzDist <= 1.40f && Time.time >= _nextClutterSwipeTime)
                {
                    _nextClutterSwipeTime = Time.time + 0.85f;
                    SwipeNearbyClutter(sockPos);
                }

                if (xzDist <= pickupDistance && Mathf.Abs(sockPos.y - transform.position.y) <= 1.35f)
                {
                    StealTargetSock();
                    return;
                }

                if (xzDist <= 1.25f && HasClearSockLine(sockPos))
                {
                    Vector3 direct = sockPos - transform.position;
                    direct.y = 0f;
                    if (direct.sqrMagnitude > 0.01f)
                    {
                        _desiredMoveDir = direct.normalized;
                        _desiredSpeed = moveSpeed * 1.15f;
                        return;
                    }
                }
            }

            if (_path.Count == 0 || _pathIndex >= _path.Count)
            {
                RebuildPathTo(sockPos);
            }

            if (FollowPath(moveSpeed * 1.15f, 0.45f))
            {
                if (blocker != null && xzDist <= 2.35f)
                {
                    blocker.Interact(gameObject);
                    _queuedHopVel = Mathf.Max(_queuedHopVel, 3.0f);
                    PlayVocal("meow", 0.72f, Random.Range(1.08f, 1.24f));
                    _pawWaitEndTime = Time.time + 0.45f;
                    _desiredMoveDir = Vector3.zero;
                    _desiredSpeed = 0f;
                    ResetStuckTracking();
                    return;
                }

                if (blocker == null && xzDist <= 1.95f && sockPos.y <= _floorY + 1.05f)
                {
                    StealTargetSock();
                    return;
                }

                if (HasClearSockLine(sockPos))
                {
                    Vector3 direct = sockPos - transform.position;
                    direct.y = 0f;
                    _desiredMoveDir = direct.sqrMagnitude > 0.001f ? direct.normalized : transform.forward;
                    _desiredSpeed = moveSpeed * 1.05f;
                }
                else
                {
                    _targetSock = null;
                    _nextTargetScanTime = Time.time + 2.0f;
                    _state = PetState.Patrol;
                    PickPatrolPath();
                }
            }
        }

        private void TickFleeWithSock()
        {
            if (_carriedSock == null)
            {
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            if (TryTriggerCloseRangeJuke()) return;

            if (_player != null && Time.time >= _nextFleeReevalTime)
            {
                _nextFleeReevalTime = Time.time + 1.0f;
                Vector3 goal = GetCurrentGoalPoint();
                float playerDistToGoal = Vector3.Distance(_player.transform.position, goal);
                float catDistToGoal = Vector3.Distance(transform.position, goal);
                if (playerDistToGoal < catDistToGoal + 0.8f || IsPlayerBlockingNextWaypoint())
                {
                    PickSmartFleePath();
                }
            }

            if (FollowPath(fleeSpeed, 0.55f))
            {
                if (_player != null &&
                    Vector3.Distance(transform.position, _player.transform.position) > 5.5f &&
                    Time.time >= _nextTauntReadyTime)
                {
                    EnterTauntState();
                    return;
                }

                _state = PetState.HoldingSock;
                _patrolWaitTimer = Random.Range(0.75f, 1.5f);
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
            }
        }

        private void TickHoldingSock()
        {
            if (_carriedSock == null)
            {
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            if (TryTriggerCloseRangeJuke()) return;

            if (_player != null)
            {
                float playerDist = Vector3.Distance(transform.position, _player.transform.position);
                if (playerDist <= 4.5f || HasNearbyFastThrownClutter(3.2f))
                {
                    _state = PetState.FleeWithSock;
                    PickSmartFleePath();
                    PlayVocal("meow", 0.7f, Random.Range(1.15f, 1.32f));
                    return;
                }

                if (playerDist > 5.5f && Time.time >= _nextTauntReadyTime)
                {
                    EnterTauntState();
                    return;
                }
            }

            if (_patrolWaitTimer > 0f)
            {
                _patrolWaitTimer -= Time.deltaTime;
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
                return;
            }

            if (_path.Count == 0 || _pathIndex >= _path.Count)
            {
                PickSmartFleePath();
            }

            if (FollowPath(moveSpeed * 0.85f, 0.45f))
            {
                _patrolWaitTimer = Random.Range(0.9f, 1.8f);
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
            }
        }

        private void EnterTauntState()
        {
            _state = PetState.Taunt;
            _desiredMoveDir = Vector3.zero;
            _desiredSpeed = 0f;
            _tauntEndTime = Time.time + Random.Range(2.4f, 3.4f);
            _nextTauntReadyTime = Time.time + 5.5f;
            _queuedHopVel = Mathf.Max(_queuedHopVel, 2.2f);
            PlayVocal("meow", 0.82f, Random.Range(1.14f, 1.28f));
        }

        private void TickTaunt()
        {
            _desiredMoveDir = Vector3.zero;
            _desiredSpeed = 0f;

            if (_carriedSock == null)
            {
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            if (TryTriggerCloseRangeJuke()) return;

            if (_player != null &&
                (Vector3.Distance(transform.position, _player.transform.position) <= 4.5f ||
                 HasNearbyFastThrownClutter(3.2f)))
            {
                _queuedHopVel = Mathf.Max(_queuedHopVel, 2.8f);
                PlayVocal("meow", 0.78f, Random.Range(1.22f, 1.38f));
                _state = PetState.FleeWithSock;
                PickSmartFleePath();
                return;
            }

            if (Time.time >= _tauntEndTime)
            {
                _state = PetState.HoldingSock;
                _patrolWaitTimer = 0.2f;
                PickSmartFleePath();
            }
        }

        private bool TryTriggerCloseRangeJuke()
        {
            if (_carriedSock == null || _player == null) return false;
            if (Time.time < _nextJukeReadyTime) return false;

            float playerDist = Vector3.Distance(transform.position, _player.transform.position);
            if (playerDist > 2.3f) return false;

            _jukeDir = PickBestJukeDirection();
            _state = PetState.JukeTelegraph;
            _jukePhaseEndTime = Time.time + JukeTelegraphDuration;
            _desiredMoveDir = Vector3.zero;
            _desiredSpeed = 0f;
            PlayVocal("chirp", 0.92f, Random.Range(0.96f, 1.12f));
            return true;
        }

        private void TickJukeTelegraph()
        {
            _desiredMoveDir = Vector3.zero;
            _desiredSpeed = 0f;

            if (_carriedSock == null)
            {
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            if (Time.time >= _jukePhaseEndTime)
            {
                _state = PetState.JukeDash;
                _jukePhaseEndTime = Time.time + JukeDashDuration;
                _queuedHopVel = Mathf.Max(_queuedHopVel, 2.6f);
                SpawnDustBurst(3);
                ResetStuckTracking();
            }
        }

        private void TickJukeDash()
        {
            if (_carriedSock == null)
            {
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            _desiredMoveDir = _jukeDir;
            _desiredSpeed = JukeSpeed;

            if (Time.time >= _jukePhaseEndTime)
            {
                _nextJukeReadyTime = Time.time + Random.Range(3.5f, 4.5f);
                _jukeRecoveryEndTime = Time.time + JukeRecoveryDuration;
                _state = PetState.FleeWithSock;
                PickSmartFleePath();
            }
        }

        private void TickScaredFlee()
        {
            if (Time.time >= _scaredEndTime)
            {
                _state = PetState.Patrol;
                PickPatrolPath();
                return;
            }

            if (FollowPath(fleeSpeed, 0.55f))
            {
                PickSmartFleePath();
            }
        }

        private void StealTargetSock()
        {
            _carriedSock = _targetSock;
            _targetSock = null;
            _carriedSock.AttachToPetMouth(_mouthAnchor);
            UpdateColliderForCarryState(true);
            if (_alertMarker != null) _alertMarker.SetActive(true);

            string itemLabel = _carriedSock.data != null
                ? _carriedSock.data.displayName.ToUpperInvariant()
                : "SOCK";
            TimerEscalationManager.Toast($"THE CAT STOLE YOUR {itemLabel}!!");
            StealToastCount++;
            PlayVocal("meow", 0.95f, Random.Range(1.05f, 1.22f), force: true);

            _queuedHopVel = Mathf.Max(_queuedHopVel, 3.8f);
            _nextJukeReadyTime = Time.time + 1.2f;
            _nextTauntReadyTime = Time.time + 3.0f;
            _state = PetState.FleeWithSock;
            PickSmartFleePath();
            ResetStuckTracking();
        }

        private void HandleBonkedByClutter(Vector3 hitOrigin)
        {
            if (Time.time < _bonkCooldownEnd) return;
            _bonkCooldownEnd = Time.time + 0.65f;
            _bonkSpinTimer = 0.70f;

            if (_carriedSock != null)
            {
                WearableItem sock = _carriedSock;
                _carriedSock = null;
                UpdateColliderForCarryState(false);
                if (_alertMarker != null) _alertMarker.SetActive(false);

                Vector3 away = transform.position - hitOrigin;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                away.Normalize();

                Vector3 startPos = transform.position + Vector3.up * 0.36f;
                Vector3 landPos = FindSafeDropPosition(transform.position + away * 0.65f);
                sock.DropFromPet(startPos, landPos);

                Sfx.PlayAt("bonk", transform.position, 0.85f, Random.Range(0.95f, 1.1f));
                Sfx.PlayAt("hiss", transform.position, 0.88f, Random.Range(0.95f, 1.15f));
                TimerEscalationManager.Toast("BONK! You knocked your sock loose!");
                SpawnDustBurst(4);
                StartScaredFlee();
            }
            else if (_state != PetState.ScaredFlee)
            {
                Sfx.PlayAt("hiss", transform.position, 0.70f, Random.Range(0.95f, 1.15f));
                SpawnDustBurst(2);
                StartScaredFlee();
            }
        }

        private bool IsFastThrownTowardCat(PhysicsClutter clutter)
        {
            if (clutter == null || clutter.IsCurrentlyHeld || clutter.Body == null || clutter.Body.isKinematic)
                return false;

            if (_nudgedIgnoreUntil.TryGetValue(clutter.GetInstanceID(), out float ignoreUntil) && Time.time < ignoreUntil)
                return false;

            Vector3 v = clutter.Body.linearVelocity;
            if (v.magnitude < 2.4f) return false;

            Vector3 toCat = transform.position + Vector3.up * 0.3f - clutter.Body.worldCenterOfMass;
            if (toCat.sqrMagnitude > 0.01f && Vector3.Dot(v.normalized, toCat.normalized) < 0.05f)
                return false;

            return true;
        }

        private void CheckProximityThrownClutter()
        {
            if (Time.time < _bonkCooldownEnd) return;

            int hits = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.36f, 0.58f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider c = _overlapBuf[i];
                if (c == null) continue;
                PhysicsClutter clutter = c.GetComponentInParent<PhysicsClutter>();
                if (IsFastThrownTowardCat(clutter))
                {
                    HandleBonkedByClutter(clutter.transform.position);
                    return;
                }
            }
        }

        private bool HasNearbyFastThrownClutter(float radius)
        {
            int hits = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.35f, radius, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider c = _overlapBuf[i];
                if (c == null) continue;
                PhysicsClutter clutter = c.GetComponentInParent<PhysicsClutter>();
                if (IsFastThrownTowardCat(clutter))
                {
                    return true;
                }
            }
            return false;
        }

        private void StartScaredFlee()
        {
            _targetSock = null;
            _queuedHopVel = Mathf.Max(_queuedHopVel, 4.4f);
            _stealReadyTime = Time.time + stealCooldown;
            _scaredEndTime = Time.time + 4.5f;
            _state = PetState.ScaredFlee;
            PickSmartFleePath();
            ResetStuckTracking();
        }

        private void UpdateColliderForCarryState(bool carrying)
        {
            if (_col == null) return;
            _col.direction = 1;
            if (carrying)
            {
                _col.radius = 0.30f;
                _col.height = 0.70f;
                _col.center = new Vector3(0f, 0.35f, 0f);
            }
            else
            {
                _col.radius = 0.24f;
                _col.height = 0.54f;
                _col.center = new Vector3(0f, 0.27f, 0f);
            }
        }

        // ───────────────────────────── Target & Pathing ─────────────────────────────

        private WearableItem FindTargetSock()
        {
            WearableItem[] items = FindObjectsByType<WearableItem>(FindObjectsSortMode.None);
            WearableItem best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < items.Length; i++)
            {
                WearableItem item = items[i];
                if (!IsSockStealable(item)) continue;

                float dist = Vector3.Distance(transform.position, item.transform.position);
                float tierPenalty = 0f;
                if (item.data.IsTrap)
                {
                    tierPenalty = 14f;
                }
                else if (GetBlockingFurniture(item) != null)
                {
                    tierPenalty = 8f;
                }
                else if (IsCoveredByClutter(item))
                {
                    tierPenalty = 4f;
                }

                float score = tierPenalty + dist * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }

            return best;
        }

        private OpenableFurniture GetBlockingFurniture(WearableItem item)
        {
            if (item == null) return null;

            OpenableFurniture parentFurn = item.GetComponentInParent<OpenableFurniture>();
            if (parentFurn != null)
            {
                return parentFurn.IsOpen ? null : parentFurn;
            }

            if (item.spot != null && item.spot.concealmentType == ConcealmentType.InsideDrawer && item.spot.transform.parent != null)
            {
                OpenableFurniture[] siblingFurns = item.spot.transform.parent.GetComponentsInChildren<OpenableFurniture>(true);
                if (siblingFurns.Length > 0)
                {
                    OpenableFurniture closestClosed = null;
                    float closestDist = float.MaxValue;
                    for (int i = 0; i < siblingFurns.Length; i++)
                    {
                        OpenableFurniture f = siblingFurns[i];
                        if (f == null) continue;
                        if (f.IsOpen) return null;
                        float d = (f.transform.position - item.transform.position).sqrMagnitude;
                        if (d < closestDist)
                        {
                            closestDist = d;
                            closestClosed = f;
                        }
                    }
                    return closestClosed;
                }
            }

            return null;
        }

        private void SwipeNearbyClutter(Vector3 center)
        {
            int hits = Physics.OverlapSphereNonAlloc(center, 0.85f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider c = _overlapBuf[i];
                if (c == null) continue;
                PhysicsClutter clutter = c.GetComponentInParent<PhysicsClutter>();
                if (clutter == null || clutter.IsCurrentlyHeld || clutter.Body == null || clutter.Body.isKinematic) continue;

                _nudgedIgnoreUntil[clutter.GetInstanceID()] = Time.time + 0.9f;
                Vector3 away = clutter.transform.position - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.right;
                away = away.normalized * 1.8f + Vector3.up * 1.1f;
                clutter.Body.AddForce(away, ForceMode.VelocityChange);
            }
        }

        private bool IsSockStealable(WearableItem item)
        {
            return EvaluateSockStealability(item, out _);
        }

        public bool EvaluateSockStealability(WearableItem item, out string reason)
        {
            if (item == null || item.IsCollected || item.IsCarriedByPet || item.data == null)
            {
                reason = "null/collected/carried/no-data";
                return false;
            }
            if (item.data.slot != WearSlot.LeftFoot && item.data.slot != WearSlot.RightFoot)
            {
                reason = $"not a foot slot ({item.data.slot})";
                return false;
            }
            if (item.transform.position.y > _floorY + 1.00f)
            {
                reason = $"too high: y={item.transform.position.y:F2} > floorY+1.00 ({_floorY + 1.00f:F2})";
                return false;
            }

            OpenableFurniture blocker = GetBlockingFurniture(item);

            int nearest = FindNearestNode(item.transform.position);
            if (nearest >= 0)
            {
                Vector2 a = new Vector2(_nodes[nearest].position.x, _nodes[nearest].position.z);
                Vector2 b = new Vector2(item.transform.position.x, item.transform.position.z);
                float xzDist = Vector2.Distance(a, b);
                if (xzDist > 2.10f)
                {
                    reason = $"too far from nav node: dist={xzDist:F2}m > 2.10m (nearest={_nodes[nearest].position:F2})";
                    return false;
                }
                if (blocker != null)
                {
                    reason = $"OK (closed furn '{blocker.name}' - will paw open, nodeDist={xzDist:F2}m)";
                    return true;
                }
                reason = $"OK (nodeDist={xzDist:F2}m, blocker=none)";
                return true;
            }

            reason = "OK (no nav nodes)";
            return true;
        }

        public string GetNavGraphDiagnosticReport()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"[PetAI NavGraph] floorY={_floorY:F3}, RawNodes={RawNodeCount}, PrunedNodes={PrunedNodeCount}, RawComponents=[{_componentSizesCsv}]");

            Dictionary<Room, int> perRoom = new Dictionary<Room, int>();
            int totalDeg = 0;
            for (int i = 0; i < _nodes.Count; i++)
            {
                totalDeg += _adj[i].Count;
                if (_nodes[i].hasRoom)
                {
                    perRoom.TryGetValue(_nodes[i].room, out int c);
                    perRoom[_nodes[i].room] = c + 1;
                }
            }
            float avgDeg = _nodes.Count > 0 ? (float)totalDeg / _nodes.Count : 0f;
            sb.AppendLine($"[PetAI NavGraph] ConnectedRooms({ConnectedRoomCount}): {ConnectedRoomsCsv} | AvgDegree={avgDeg:F2}");
            foreach (KeyValuePair<Room, int> kv in perRoom)
            {
                sb.AppendLine($"  - Room {kv.Key}: {kv.Value} nodes");
            }

            (string label, Vector3 center, Vector3 spanDir)[] doorways =
            {
                ("Living<->Hall", new Vector3(3.15f, _floorY, 8.4f), Vector3.forward),
                ("Kitchen<->Hall", new Vector3(9.45f, _floorY, 8.4f), Vector3.forward),
                ("Hall<->Bedroom", new Vector3(5.25f, _floorY, 10.5f), Vector3.forward),
                ("Hall<->Bathroom", new Vector3(9.45f, _floorY, 10.5f), Vector3.forward),
                ("Bedroom<->Bathroom", new Vector3(8.4f, _floorY, 13.65f), Vector3.right)
            };

            sb.AppendLine("[PetAI Doorway Probes]:");
            for (int i = 0; i < doorways.Length; i++)
            {
                Vector3 p = doorways[i].center;
                Vector3 capBottom = p + Vector3.up * 0.44f;
                Vector3 capTop = p + Vector3.up * 0.54f;
                int overlaps = Physics.OverlapCapsuleNonAlloc(capBottom, capTop, 0.36f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
                List<string> capBlockers = new List<string>();
                for (int k = 0; k < overlaps; k++)
                {
                    Collider c = _overlapBuf[k];
                    if (IsStaticWorldObstacle(c)) capBlockers.Add(c.name);
                }

                Vector3 a = p - doorways[i].spanDir * 0.9f;
                Vector3 b = p + doorways[i].spanDir * 0.9f;
                Vector3 dir = (b - a).normalized;
                RaycastHit[] hits = Physics.SphereCastAll(a + Vector3.up * 0.42f, 0.28f, dir, 1.8f, ~0, QueryTriggerInteraction.Ignore);
                List<string> castBlockers = new List<string>();
                for (int h = 0; h < hits.Length; h++)
                {
                    if (IsStaticWorldObstacle(hits[h].collider) && hits[h].normal.y < 0.7f)
                    {
                        castBlockers.Add($"{hits[h].collider.name}(ny={hits[h].normal.y:F2})");
                    }
                }

                int nearNode = FindNearestNode(p);
                float nearDist = nearNode >= 0 ? Vector3.Distance(_nodes[nearNode].position, p) : -1f;
                sb.AppendLine($"  - {doorways[i].label} @{p:F2}: nearestNode={nearDist:F2}m | CapsuleHits=[{string.Join(",", capBlockers)}] | SphereCastHits=[{string.Join(",", castBlockers)}]");
            }

            return sb.ToString();
        }

        public string GetSocksDiagnosticReport()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            WearableItem[] items = FindObjectsByType<WearableItem>(FindObjectsSortMode.None);
            sb.AppendLine($"[PetAI Socks] Total WearableItems in scene: {items.Length}");
            for (int i = 0; i < items.Length; i++)
            {
                WearableItem item = items[i];
                if (item == null || item.data == null) continue;
                if (item.data.slot != WearSlot.LeftFoot && item.data.slot != WearSlot.RightFoot) continue;

                string spotInfo = item.spot != null ? $"{item.spot.room}/{item.spot.concealmentType}" : "no-spot";
                string parentInfo = item.transform.parent != null ? item.transform.parent.name : "scene-root";
                OpenableFurniture furn = item.GetComponentInParent<OpenableFurniture>();
                string furnInfo = furn != null ? $"{furn.name}(open={furn.IsOpen},mode={furn.Mode})" : "none";
                bool covered = IsCoveredByClutter(item);
                int nearest = FindNearestNode(item.transform.position);
                float distXZ = nearest >= 0
                    ? Vector2.Distance(new Vector2(_nodes[nearest].position.x, _nodes[nearest].position.z), new Vector2(item.transform.position.x, item.transform.position.z))
                    : -1f;
                bool stealable = EvaluateSockStealability(item, out string reason);

                sb.AppendLine($"  - {item.InteractionPrompt} (trap={item.data.IsTrap}, slot={item.data.slot}) @ {item.transform.position:F2} | spot={spotInfo} | parent={parentInfo} | furn={furnInfo} | covered={covered} | nodeXZ={distXZ:F2}m | stealable={stealable} -> {reason}");
            }
            return sb.ToString();
        }

        private bool IsCoveredByClutter(WearableItem item)
        {
            if (item.spot != null && item.spot.concealmentType == ConcealmentType.UnderClutter)
            {
                int hits = Physics.OverlapSphereNonAlloc(item.transform.position + Vector3.up * 0.25f, 0.45f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits; i++)
                {
                    if (_overlapBuf[i] != null && _overlapBuf[i].GetComponentInParent<PhysicsClutter>() != null)
                        return true;
                }
            }
            return false;
        }

        private bool HasClearSockLine(Vector3 sockPos)
        {
            Vector3 from = transform.position + Vector3.up * 0.28f;
            Vector3 to = sockPos + Vector3.up * 0.16f;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.05f) return true;

            RaycastHit[] hits = Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsStaticWorldObstacle(hits[i].collider)) return false;
            }
            return true;
        }

        private bool FollowPath(float speed, float waypointRadius)
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count)
            {
                _desiredMoveDir = Vector3.zero;
                _desiredSpeed = 0f;
                return true;
            }

            Vector3 target = _path[_pathIndex];
            Vector3 toTarget = target - transform.position;
            toTarget.y = 0f;

            while (toTarget.magnitude <= waypointRadius)
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count)
                {
                    _desiredMoveDir = Vector3.zero;
                    _desiredSpeed = 0f;
                    return true;
                }
                target = _path[_pathIndex];
                toTarget = target - transform.position;
                toTarget.y = 0f;
            }

            _desiredMoveDir = toTarget.normalized;
            _desiredSpeed = speed;
            return false;
        }

        private bool IsPlayerBlockingNextWaypoint()
        {
            if (_player == null || _pathIndex >= _path.Count) return false;
            Vector3 wp = _path[_pathIndex];
            float pToWp = Vector3.Distance(_player.transform.position, wp);
            float cToWp = Vector3.Distance(transform.position, wp);
            return pToWp < 2.2f && pToWp < cToWp;
        }

        private void ResetStuckTracking()
        {
            _stuckSamplePos = transform.position;
            _stuckTimer = 0f;
            _hardStuckTimer = 0f;
            _softHopTriggered = false;
        }

        private void TickUnstuckRecovery()
        {
            float moved = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(_stuckSamplePos.x, _stuckSamplePos.z));

            if (moved >= 0.25f)
            {
                ResetStuckTracking();
                return;
            }

            _stuckTimer += Time.deltaTime;
            _hardStuckTimer += Time.deltaTime;

            if (_stuckTimer >= 0.55f && !_softHopTriggered)
            {
                _softHopTriggered = true;
                SoftStuckCount++;
                _queuedHopVel = Mathf.Max(_queuedHopVel, 4.2f);

                int hub = FindNearestHubNode(transform.position);
                if (hub >= 0)
                {
                    Vector3 toHub = _nodes[hub].position - transform.position;
                    toHub.y = 0f;
                    if (toHub.sqrMagnitude > 0.01f)
                    {
                        _rb.AddForce(toHub.normalized * 2.4f, ForceMode.VelocityChange);
                    }
                }

                if (_state == PetState.Stalking)
                {
                    _stalkStuckCount++;
                    if (_stalkStuckCount >= 2 || !IsSockStealable(_targetSock))
                    {
                        _targetSock = null;
                        _nextTargetScanTime = Time.time + 2.5f;
                        _state = PetState.Patrol;
                        PickPatrolPath();
                        ResetStuckTracking();
                        return;
                    }
                }

                if (_carriedSock != null) PickSmartFleePath();
                else if (_state == PetState.Stalking && _targetSock != null) RebuildPathTo(_targetSock.transform.position);
                else PickPatrolPath();
            }

            if (_hardStuckTimer >= 1.40f)
            {
                HardStuckCount++;
                int safeNode = FindNearestHubNode(transform.position);
                if (safeNode < 0) safeNode = FindNearestNode(transform.position);
                if (safeNode >= 0)
                {
                    _rb.position = _nodes[safeNode].position + Vector3.up * 0.14f;
                    _rb.linearVelocity = Vector3.up * 2.4f;
                    transform.position = _rb.position;
                    SpawnDustBurst(2);
                }

                if (_carriedSock != null)
                {
                    _state = PetState.FleeWithSock;
                    PickSmartFleePath();
                }
                else
                {
                    _targetSock = null;
                    _state = PetState.Patrol;
                    PickPatrolPath();
                }

                ResetStuckTracking();
            }
        }

        private Vector3 PickBestJukeDirection()
        {
            Vector3 awayFromPlayer = transform.position - _player.transform.position;
            awayFromPlayer.y = 0f;
            if (awayFromPlayer.sqrMagnitude < 0.01f) awayFromPlayer = transform.forward;
            awayFromPlayer.Normalize();

            float[] angles = { 85f, -85f, 115f, -115f, 145f, -145f, 25f, -25f };
            Vector3 origin = transform.position + Vector3.up * 0.34f;

            Vector3 bestDir = Quaternion.Euler(0f, 90f, 0f) * awayFromPlayer;
            float bestClearDist = -1f;

            for (int i = 0; i < angles.Length; i++)
            {
                Vector3 candidate = (Quaternion.Euler(0f, angles[i], 0f) * (-awayFromPlayer)).normalized;
                float clearDist = 3.2f;

                RaycastHit[] hits = Physics.SphereCastAll(origin, 0.28f, candidate, 3.2f, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits.Length; h++)
                {
                    if (IsStaticWorldObstacle(hits[h].collider) && hits[h].normal.y < 0.7f)
                    {
                        clearDist = Mathf.Min(clearDist, hits[h].distance);
                    }
                }

                // Slight preference for sweeping past/perpendicular to the player rather than straight back into them
                float angleScore = Mathf.Abs(angles[i]) <= 120f ? 0.35f : 0f;
                float score = clearDist + angleScore + Random.Range(0f, 0.25f);
                if (score > bestClearDist)
                {
                    bestClearDist = score;
                    bestDir = candidate;
                }
            }

            return bestDir;
        }

        private Vector3 ApplyWallAvoidance(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.001f) return Vector3.zero;

            Vector3 origin = transform.position + Vector3.up * 0.30f;
            Vector3 steer = dir;

            steer = AddWhiskerRepulsion(origin, dir, 0f, 0.52f, 0.85f, steer);
            steer = AddWhiskerRepulsion(origin, dir, -38f, 0.58f, 0.55f, steer);
            steer = AddWhiskerRepulsion(origin, dir, 38f, 0.58f, 0.55f, steer);

            steer.y = 0f;
            return steer.sqrMagnitude > 0.001f ? steer.normalized : dir.normalized;
        }

        private Vector3 AddWhiskerRepulsion(Vector3 origin, Vector3 forward, float angleDeg, float dist, float weight, Vector3 steer)
        {
            Vector3 rayDir = Quaternion.Euler(0f, angleDeg, 0f) * forward;
            RaycastHit[] hits = Physics.RaycastAll(origin, rayDir, dist, ~0, QueryTriggerInteraction.Ignore);

            float closestDist = float.MaxValue;
            RaycastHit closest = default;
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsStaticWorldObstacle(hits[i].collider) && hits[i].distance < closestDist)
                {
                    closestDist = hits[i].distance;
                    closest = hits[i];
                    found = true;
                }
            }

            if (found)
            {
                Vector3 push = closest.normal;
                push.y = 0f;
                if (push.sqrMagnitude < 0.01f) push = Vector3.Cross(Vector3.up, rayDir);
                float strength = (1f - closest.distance / dist) * weight;
                steer += push.normalized * strength;
            }
            return steer;
        }

        private bool IsStaticWorldObstacle(Collider col)
        {
            if (col == null || col.isTrigger) return false;
            if (col == _floorCollider) return false;
            string colName = col.name;
            if (colName == "FloorCollider" || colName.StartsWith("Sidewalk") || colName.StartsWith("Ground") || colName.StartsWith("Yard"))
                return false;
            if (col.transform.IsChildOf(transform)) return false;
            if (col.GetComponentInParent<PlayerController>() != null) return false;
            if (col.GetComponentInParent<WearableItem>() != null) return false;
            if (col.GetComponentInParent<PhysicsClutter>() != null) return false;
            if (col.GetComponentInParent<OpenableFurniture>() != null) return false;
            if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) return false;
            if (col.bounds.max.y <= _floorY + 0.05f) return false;
            if (col.bounds.min.y >= _floorY + 0.85f) return false;
            return true;
        }

        private Vector3 FindSafeDropPosition(Vector3 desiredPos)
        {
            int idx = FindNearestNode(desiredPos);
            if (idx >= 0 && Vector3.Distance(_nodes[idx].position, desiredPos) <= 1.2f)
            {
                return _nodes[idx].position + Vector3.up * 0.06f;
            }

            Vector3 fallback = desiredPos;
            fallback.y = _floorY + 0.08f;
            return fallback;
        }

        // ─────────────────────────── Runtime Waypoint Graph ───────────────────────────

        private void IgnoreHingedDoorCollisions()
        {
            OpenableFurniture[] openables = FindObjectsByType<OpenableFurniture>(FindObjectsSortMode.None);
            for (int i = 0; i < openables.Length; i++)
            {
                OpenableFurniture of = openables[i];
                if (of == null || of.Mode != OpenMode.Hinge) continue;

                Collider[] doorCols = of.GetComponentsInChildren<Collider>(true);
                for (int j = 0; j < doorCols.Length; j++)
                {
                    if (doorCols[j] != null) Physics.IgnoreCollision(_col, doorCols[j], true);
                }
            }
        }

        private void DetectFloorColliderAndHeight()
        {
            _floorCollider = null;
            GameObject floorObj = GameObject.Find("FloorCollider");
            if (floorObj != null && floorObj.TryGetComponent(out Collider fc))
            {
                _floorCollider = fc;
                _floorY = fc.bounds.max.y;
                return;
            }

            Vector3[] probes = { transform.position + Vector3.up * 1.2f, new Vector3(6.3f, 1.5f, 9.45f) };
            float highestFloor = float.MinValue;
            for (int p = 0; p < probes.Length; p++)
            {
                RaycastHit[] floorHits = Physics.RaycastAll(probes[p], Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < floorHits.Length; i++)
                {
                    RaycastHit h = floorHits[i];
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    if (h.collider.GetComponentInParent<PlayerController>() != null) continue;
                    if (h.collider.GetComponentInParent<WearableItem>() != null) continue;
                    if (h.collider.GetComponentInParent<PhysicsClutter>() != null) continue;
                    if (h.collider.GetComponentInParent<OpenableFurniture>() != null) continue;
                    if (h.normal.y > 0.85f && h.point.y >= -0.05f && h.point.y <= 0.40f && h.point.y > highestFloor)
                    {
                        highestFloor = h.point.y;
                        _floorCollider = h.collider;
                    }
                }
            }
            if (highestFloor > float.MinValue) _floorY = highestFloor;
        }

        private void BuildNavGraph()
        {
            _nodes.Clear();
            _adj.Clear();

            DetectFloorColliderAndHeight();

            HidingSpot[] spots = FindObjectsByType<HidingSpot>(FindObjectsSortMode.None);
            float minX = 0.6f, maxX = 12.0f, minZ = 0.6f, maxZ = 16.2f;

            if (spots.Length > 0)
            {
                minX = float.MaxValue;
                maxX = float.MinValue;
                minZ = float.MaxValue;
                maxZ = float.MinValue;

                for (int i = 0; i < spots.Length; i++)
                {
                    Vector3 p = spots[i].transform.position;
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z);
                    maxZ = Mathf.Max(maxZ, p.z);
                }

                minX -= 0.6f;
                maxX += 0.6f;
                minZ -= 0.6f;
                maxZ += 0.6f;
            }

            // 1. Sample doorway centers first so cross-room portals always get centered nodes
            Vector3[] doorwayCenters =
            {
                new Vector3(3.15f, _floorY + 0.05f, 8.4f),
                new Vector3(9.45f, _floorY + 0.05f, 8.4f),
                new Vector3(5.25f, _floorY + 0.05f, 10.5f),
                new Vector3(9.45f, _floorY + 0.05f, 10.5f),
                new Vector3(8.4f, _floorY + 0.05f, 13.65f)
            };
            for (int i = 0; i < doorwayCenters.Length; i++)
            {
                if (TrySampleWalkablePoint(doorwayCenters[i], out Vector3 walkable))
                {
                    Room nearestRoom = FindNearestRoom(spots, walkable, out bool hasRoom);
                    TryAddNode(walkable, nearestRoom, hasRoom, 0.60f);
                }
            }

            // 2. Sample a wide-clearance XZ grid so paths stay in room centers & doorway middles
            for (float x = minX; x <= maxX; x += roomSampleStep)
            {
                for (float z = minZ; z <= maxZ; z += roomSampleStep)
                {
                    if (TrySampleWalkablePoint(new Vector3(x, _floorY + 0.05f, z), out Vector3 walkable))
                    {
                        Room nearestRoom = FindNearestRoom(spots, walkable, out bool hasRoom);
                        TryAddNode(walkable, nearestRoom, hasRoom, 0.60f);
                    }
                }
            }

            // 3. Include open floor-level HidingSpots only (never HighShelf, InsideDrawer, or enclosed furniture)
            for (int i = 0; i < spots.Length; i++)
            {
                HidingSpot s = spots[i];
                if (s == null) continue;
                if (s.concealmentType == ConcealmentType.HighShelf || s.concealmentType == ConcealmentType.InsideDrawer) continue;
                if (s.GetComponentInParent<OpenableFurniture>() != null) continue;
                if (s.transform.position.y > _floorY + 0.35f) continue;

                if (TrySampleWalkablePoint(s.transform.position, out Vector3 walkable))
                {
                    TryAddNode(walkable, s.room, true, 0.60f);
                }
            }

            if (_nodes.Count == 0)
            {
                _nodes.Add(new NavNode { position = transform.position, hasRoom = false });
                _adj.Add(new List<int>());
                return;
            }

            // 4. Connect nearby unobstructed corridors
            float maxEdgeDist = roomSampleStep * 1.75f;
            for (int i = 0; i < _nodes.Count; i++)
            {
                for (int j = i + 1; j < _nodes.Count; j++)
                {
                    Vector3 a = _nodes[i].position;
                    Vector3 b = _nodes[j].position;
                    if (Vector3.Distance(a, b) > maxEdgeDist) continue;
                    if (HasClearCorridor(a, b))
                    {
                        _adj[i].Add(j);
                        _adj[j].Add(i);
                    }
                }
            }

            // 5. Prune isolated islands so every nav node is mutually reachable
            RawNodeCount = _nodes.Count;
            PruneToLargestComponent();
        }

        private bool TrySampleWalkablePoint(Vector3 approxPos, out Vector3 floorPos)
        {
            floorPos = approxPos;
            Ray probeRay = new Ray(new Vector3(approxPos.x, _floorY + 0.8f, approxPos.z), Vector3.down);
            RaycastHit floorHit = default;
            bool foundFloor = false;

            if (_floorCollider != null)
            {
                foundFloor = _floorCollider.Raycast(probeRay, out floorHit, 1.5f);
            }
            else
            {
                RaycastHit[] hits = Physics.RaycastAll(new Vector3(approxPos.x, _floorY + 1.5f, approxPos.z), Vector3.down, 2.2f, ~0, QueryTriggerInteraction.Ignore);
                float highest = float.MinValue;
                for (int i = 0; i < hits.Length; i++)
                {
                    RaycastHit h = hits[i];
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (h.normal.y <= 0.75f) continue;
                    if (Mathf.Abs(h.point.y - _floorY) > 0.15f) continue;
                    if (h.point.y > highest)
                    {
                        highest = h.point.y;
                        floorHit = h;
                        foundFloor = true;
                    }
                }
            }

            if (!foundFloor) return false;

            // Require ~0.36m radial clearance away from walls, shelves, and static furniture (+0.08m..+0.90m vertical)
            Vector3 capBottom = floorHit.point + Vector3.up * 0.44f;
            Vector3 capTop = floorHit.point + Vector3.up * 0.54f;
            int overlaps = Physics.OverlapCapsuleNonAlloc(capBottom, capTop, 0.36f, _overlapBuf, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlaps; i++)
            {
                Collider c = _overlapBuf[i];
                if (c == null || c == floorHit.collider) continue;
                if (IsStaticWorldObstacle(c)) return false;
            }

            floorPos = floorHit.point + Vector3.up * 0.04f;
            return true;
        }

        private void TryAddNode(Vector3 pos, Room room, bool hasRoom, float minSpacing)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (Vector3.Distance(_nodes[i].position, pos) < minSpacing) return;
            }
            _nodes.Add(new NavNode { position = pos, room = room, hasRoom = hasRoom });
            _adj.Add(new List<int>());
        }

        private static Room FindNearestRoom(HidingSpot[] spots, Vector3 pos, out bool hasRoom)
        {
            hasRoom = false;
            Room best = Room.Hallway;
            float bestDist = float.MaxValue;
            for (int i = 0; i < spots.Length; i++)
            {
                float d = (spots[i].transform.position - pos).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = spots[i].room;
                    hasRoom = true;
                }
            }
            return best;
        }

        private bool HasClearCorridor(Vector3 a, Vector3 b)
        {
            Vector3 dir = b - a;
            float dist = dir.magnitude;
            if (dist < 0.05f) return true;
            dir /= dist;

            Vector3 origin = a + Vector3.up * 0.42f;
            RaycastHit[] hits = Physics.SphereCastAll(origin, 0.28f, dir, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (IsStaticWorldObstacle(hits[i].collider))
                {
                    return false;
                }
            }
            return true;
        }

        private void PruneToLargestComponent()
        {
            int count = _nodes.Count;
            if (count <= 1) return;

            int[] comp = new int[count];
            for (int i = 0; i < count; i++) comp[i] = -1;

            int bestComp = 0;
            int bestSize = 0;
            int compId = 0;
            List<int> compSizes = new List<int>();
            Queue<int> queue = new Queue<int>(count);

            for (int i = 0; i < count; i++)
            {
                if (comp[i] != -1) continue;
                int size = 0;
                comp[i] = compId;
                queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    size++;
                    List<int> neighbors = _adj[cur];
                    for (int k = 0; k < neighbors.Count; k++)
                    {
                        int n = neighbors[k];
                        if (comp[n] == -1)
                        {
                            comp[n] = compId;
                            queue.Enqueue(n);
                        }
                    }
                }

                compSizes.Add(size);
                if (size > bestSize)
                {
                    bestSize = size;
                    bestComp = compId;
                }
                compId++;
            }

            compSizes.Sort((x, y) => y.CompareTo(x));
            _componentSizesCsv = string.Join(",", compSizes);

            if (bestSize == count) return;

            List<NavNode> kept = new List<NavNode>(bestSize);
            int[] remap = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (comp[i] == bestComp)
                {
                    remap[i] = kept.Count;
                    kept.Add(_nodes[i]);
                }
                else
                {
                    remap[i] = -1;
                }
            }

            List<List<int>> newAdj = new List<List<int>>(kept.Count);
            for (int i = 0; i < kept.Count; i++) newAdj.Add(new List<int>());

            for (int i = 0; i < count; i++)
            {
                int ni = remap[i];
                if (ni < 0) continue;
                List<int> oldNeighbors = _adj[i];
                for (int k = 0; k < oldNeighbors.Count; k++)
                {
                    int nj = remap[oldNeighbors[k]];
                    if (nj >= 0) newAdj[ni].Add(nj);
                }
            }

            _nodes.Clear();
            _nodes.AddRange(kept);
            _adj.Clear();
            _adj.AddRange(newAdj);
        }

        private void SnapToSafeSpawnNode()
        {
            int bestIdx = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                int deg = _adj[i].Count;
                float dist = Vector3.Distance(_nodes[i].position, transform.position);
                float roomPenalty = _nodes[i].hasRoom && (_nodes[i].room == Room.Hallway || _nodes[i].room == Room.LivingRoom)
                    ? 0f
                    : 4f;
                float degPenalty = deg < 2 ? 6f : -Mathf.Min(deg, 5) * 0.35f;
                float score = dist + roomPenalty + degPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                _rb.position = _nodes[bestIdx].position + Vector3.up * 0.05f;
                _rb.linearVelocity = Vector3.zero;
                transform.position = _rb.position;
            }
        }

        private int FindNearestNode(Vector3 pos)
        {
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float sq = (_nodes[i].position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        private int FindNearestHubNode(Vector3 pos)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                int deg = _adj[i].Count;
                float dist = Vector3.Distance(_nodes[i].position, pos);
                float score = dist - Mathf.Min(deg, 5) * 0.35f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private void PickPatrolPath()
        {
            if (_nodes.Count == 0) return;
            int idx = Random.Range(0, _nodes.Count);
            RebuildPathToNode(idx);
        }

        private void PickSmartFleePath()
        {
            if (_nodes.Count == 0) return;

            Vector3 playerPos = _player != null ? _player.transform.position : transform.position;
            Vector3 playerFwd = Vector3.forward;
            if (_player != null)
            {
                playerFwd = _player.CameraTransform != null
                    ? _player.CameraTransform.forward
                    : _player.transform.forward;
            }
            else if (Camera.main != null)
            {
                playerFwd = Camera.main.transform.forward;
            }
            playerFwd.y = 0f;
            playerFwd = playerFwd.sqrMagnitude > 0.01f ? playerFwd.normalized : Vector3.forward;

            int catNode = FindNearestNode(transform.position);
            int playerNode = FindNearestNode(playerPos);
            bool hasCatRoom = catNode >= 0 && _nodes[catNode].hasRoom;
            bool hasPlayerRoom = playerNode >= 0 && _nodes[playerNode].hasRoom;
            Room catRoom = hasCatRoom ? _nodes[catNode].room : Room.Hallway;
            Room playerRoom = hasPlayerRoom ? _nodes[playerNode].room : Room.Hallway;

            int bestIdx = -1;
            float bestScore = float.MinValue;

            for (int i = 0; i < _nodes.Count; i++)
            {
                Vector3 nPos = _nodes[i].position;
                float distPlayer = Vector3.Distance(nPos, playerPos);
                Vector3 fromPlayer = nPos - playerPos;
                fromPlayer.y = 0f;

                float conePenalty = 0f;
                if (fromPlayer.sqrMagnitude > 0.05f)
                {
                    float dot = Vector3.Dot(playerFwd, fromPlayer.normalized);
                    if (dot > 0.35f) conePenalty = dot * 6.5f;
                }

                float degreeBonus = Mathf.Min(_adj[i].Count, 8) * 1.35f;
                float roomBonus = 0f;
                if (_nodes[i].hasRoom)
                {
                    if (hasPlayerRoom && _nodes[i].room != playerRoom) roomBonus += 3.5f;
                    if (hasCatRoom && _nodes[i].room != catRoom) roomBonus += 1.8f;
                }

                float stallPenalty = Vector3.Distance(nPos, transform.position) < 3.2f ? 5.0f : 0f;
                float score = distPlayer * 1.4f + degreeBonus + roomBonus - conePenalty - stallPenalty + Random.Range(0f, 1.5f);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0) RebuildPathToNode(bestIdx);
        }

        private void RebuildPathTo(Vector3 worldTarget)
        {
            int goal = FindNearestNode(worldTarget);
            RebuildPathToNode(goal);
        }

        private void RebuildPathToNode(int goalIdx)
        {
            _path.Clear();
            _pathIndex = 0;
            _goalNode = goalIdx;
            if (goalIdx < 0 || goalIdx >= _nodes.Count) return;

            int startIdx = FindNearestNode(transform.position);
            if (startIdx < 0 || startIdx == goalIdx)
            {
                _path.Add(_nodes[goalIdx].position);
                return;
            }

            int count = _nodes.Count;
            int[] prev = new int[count];
            for (int i = 0; i < count; i++) prev[i] = -2;

            Queue<int> queue = new Queue<int>(count);
            prev[startIdx] = -1;
            queue.Enqueue(startIdx);

            bool found = false;
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (cur == goalIdx)
                {
                    found = true;
                    break;
                }

                List<int> neighbors = _adj[cur];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int next = neighbors[i];
                    if (prev[next] == -2)
                    {
                        prev[next] = cur;
                        queue.Enqueue(next);
                    }
                }
            }

            if (!found)
            {
                _path.Add(_nodes[goalIdx].position);
                return;
            }

            List<int> rev = new List<int>();
            for (int at = goalIdx; at != -1; at = prev[at]) rev.Add(at);
            for (int i = rev.Count - 1; i >= 0; i--) _path.Add(_nodes[rev[i]].position);
        }

        private Vector3 GetCurrentGoalPoint()
        {
            if (_goalNode >= 0 && _goalNode < _nodes.Count) return _nodes[_goalNode].position;
            return transform.position;
        }

        // ───────────────────────────── Procedural Cat Visual & FX ─────────────────────────────

        private void BuildVisual()
        {
            _visualRoot = new GameObject("Visual").transform;
            _visualRoot.SetParent(transform, false);

            Material orange = MakeMat(new Color(0.93f, 0.52f, 0.18f), 0.25f);
            Material cream = MakeMat(new Color(0.98f, 0.89f, 0.74f), 0.2f);
            Material stripe = MakeMat(new Color(0.72f, 0.31f, 0.08f), 0.25f);
            Material nosePink = MakeMat(new Color(0.95f, 0.45f, 0.56f), 0.35f);
            Material eyeWhite = MakeMat(new Color(0.98f, 0.96f, 0.86f), 0.4f);
            Material pupilDark = MakeMat(new Color(0.08f, 0.09f, 0.1f), 0.7f);
            Material collar = MakeMat(new Color(0.88f, 0.14f, 0.22f), 0.35f);
            Material gold = MakeMat(new Color(0.98f, 0.82f, 0.18f), 0.75f);
            Material alertGold = MakeMat(new Color(1.0f, 0.72f, 0.10f), 0.2f, unlit: true);
            _dustMat = MakeMat(new Color(0.97f, 0.95f, 0.90f), 0.05f);

            _bodyPivot = new GameObject("BodyPivot").transform;
            _bodyPivot.SetParent(_visualRoot, false);
            _bodyPivot.localPosition = new Vector3(0f, 0.24f, 0f);

            // Chubby goofy torso
            MakePrim(PrimitiveType.Capsule, _bodyPivot, "Torso", new Vector3(0f, 0f, 0f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.26f, 0.22f, 0.26f), orange);
            MakePrim(PrimitiveType.Sphere, _bodyPivot, "Belly", new Vector3(0f, -0.05f, 0.02f),
                Quaternion.identity, new Vector3(0.21f, 0.17f, 0.34f), cream);
            MakePrim(PrimitiveType.Cube, _bodyPivot, "Stripe1", new Vector3(0f, 0.11f, -0.02f),
                Quaternion.identity, new Vector3(0.16f, 0.03f, 0.06f), stripe);
            MakePrim(PrimitiveType.Cube, _bodyPivot, "Stripe2", new Vector3(0f, 0.11f, 0.08f),
                Quaternion.identity, new Vector3(0.15f, 0.03f, 0.05f), stripe);

            // Collar + golden bell
            MakePrim(PrimitiveType.Cylinder, _bodyPivot, "Collar", new Vector3(0f, 0.06f, 0.18f),
                Quaternion.Euler(55f, 0f, 0f), new Vector3(0.21f, 0.03f, 0.21f), collar);
            MakePrim(PrimitiveType.Sphere, _bodyPivot, "Bell", new Vector3(0f, -0.02f, 0.26f),
                Quaternion.identity, new Vector3(0.065f, 0.065f, 0.065f), gold);

            // Head pivot
            _headPivot = new GameObject("HeadPivot").transform;
            _headPivot.SetParent(_bodyPivot, false);
            _headPivot.localPosition = new Vector3(0f, 0.12f, 0.23f);

            MakePrim(PrimitiveType.Sphere, _headPivot, "Head", new Vector3(0f, 0.05f, 0.04f),
                Quaternion.identity, new Vector3(0.26f, 0.23f, 0.24f), orange);
            MakePrim(PrimitiveType.Sphere, _headPivot, "Muzzle", new Vector3(0f, 0.0f, 0.14f),
                Quaternion.identity, new Vector3(0.13f, 0.09f, 0.09f), cream);
            MakePrim(PrimitiveType.Sphere, _headPivot, "Nose", new Vector3(0f, 0.025f, 0.18f),
                Quaternion.identity, new Vector3(0.042f, 0.032f, 0.03f), nosePink);

            // Pointy ears
            MakePrim(PrimitiveType.Cube, _headPivot, "EarL", new Vector3(-0.08f, 0.17f, 0.03f),
                Quaternion.Euler(0f, 0f, 18f), new Vector3(0.075f, 0.11f, 0.05f), orange);
            MakePrim(PrimitiveType.Cube, _headPivot, "EarR", new Vector3(0.08f, 0.17f, 0.03f),
                Quaternion.Euler(0f, 0f, -18f), new Vector3(0.075f, 0.11f, 0.05f), orange);
            MakePrim(PrimitiveType.Cube, _headPivot, "InnerEarL", new Vector3(-0.08f, 0.165f, 0.055f),
                Quaternion.Euler(0f, 0f, 18f), new Vector3(0.042f, 0.065f, 0.015f), nosePink);
            MakePrim(PrimitiveType.Cube, _headPivot, "InnerEarR", new Vector3(0.08f, 0.165f, 0.055f),
                Quaternion.Euler(0f, 0f, -18f), new Vector3(0.042f, 0.065f, 0.015f), nosePink);

            // Big goofy wide eyes
            _eyeL = MakePrim(PrimitiveType.Sphere, _headPivot, "EyeL", new Vector3(-0.058f, 0.078f, 0.138f),
                Quaternion.identity, new Vector3(0.062f, 0.068f, 0.035f), eyeWhite).transform;
            _eyeR = MakePrim(PrimitiveType.Sphere, _headPivot, "EyeR", new Vector3(0.058f, 0.078f, 0.138f),
                Quaternion.identity, new Vector3(0.062f, 0.068f, 0.035f), eyeWhite).transform;
            MakePrim(PrimitiveType.Sphere, _eyeL, "PupilL", new Vector3(0f, 0f, 0.35f),
                Quaternion.identity, new Vector3(0.45f, 0.65f, 0.4f), pupilDark);
            MakePrim(PrimitiveType.Sphere, _eyeR, "PupilR", new Vector3(0f, 0f, 0.35f),
                Quaternion.identity, new Vector3(0.45f, 0.65f, 0.4f), pupilDark);

            // Mouth anchor where a stolen sock dangles
            _mouthAnchor = new GameObject("MouthAnchor").transform;
            _mouthAnchor.SetParent(_headPivot, false);
            _mouthAnchor.localPosition = new Vector3(0f, -0.02f, 0.18f);

            // Sassy upright wagging tail
            _tailPivot = new GameObject("TailPivot").transform;
            _tailPivot.SetParent(_bodyPivot, false);
            _tailPivot.localPosition = new Vector3(0f, 0.05f, -0.2f);
            MakePrim(PrimitiveType.Capsule, _tailPivot, "TailBase", new Vector3(0f, 0.13f, -0.06f),
                Quaternion.Euler(-28f, 0f, 0f), new Vector3(0.055f, 0.16f, 0.055f), orange);
            MakePrim(PrimitiveType.Sphere, _tailPivot, "TailTip", new Vector3(0f, 0.27f, -0.12f),
                Quaternion.identity, new Vector3(0.062f, 0.062f, 0.062f), cream);

            // 4 stubby paw-legs
            Vector3[] legOffsets =
            {
                new Vector3(-0.085f, 0.17f, 0.13f),
                new Vector3(0.085f, 0.17f, 0.13f),
                new Vector3(-0.085f, 0.17f, -0.13f),
                new Vector3(0.085f, 0.17f, -0.13f)
            };
            _legs = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                Transform hip = new GameObject("Leg_" + i).transform;
                hip.SetParent(_visualRoot, false);
                hip.localPosition = legOffsets[i];
                MakePrim(PrimitiveType.Capsule, hip, "Limb", new Vector3(0f, -0.075f, 0f),
                    Quaternion.identity, new Vector3(0.065f, 0.085f, 0.065f), orange);
                MakePrim(PrimitiveType.Sphere, hip, "Paw", new Vector3(0f, -0.15f, 0.02f),
                    Quaternion.identity, new Vector3(0.075f, 0.05f, 0.085f), cream);
                _legs[i] = hip;
            }

            // Floating "!" readability cue above cat when holding a stolen sock
            _alertMarker = new GameObject("AlertMarker");
            _alertMarker.transform.SetParent(_visualRoot, false);
            _alertMarker.transform.localPosition = new Vector3(0f, 0.92f, 0f);
            MakePrim(PrimitiveType.Cube, _alertMarker.transform, "AlertBar", new Vector3(0f, 0.12f, 0f),
                Quaternion.identity, new Vector3(0.065f, 0.20f, 0.045f), alertGold);
            MakePrim(PrimitiveType.Sphere, _alertMarker.transform, "AlertDot", new Vector3(0f, -0.04f, 0f),
                Quaternion.identity, new Vector3(0.075f, 0.075f, 0.05f), alertGold);
            _alertMarker.SetActive(false);
        }

        private GameObject MakePrim(PrimitiveType type, Transform parent, string partName, Vector3 localPos, Quaternion localRot, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
            return go;
        }

        private void PlayVocal(string clipName, float volume, float pitch, bool force = false)
        {
            float now = Time.unscaledTime;
            if (!force && now < _nextVocalTime) return;
            _nextVocalTime = now + 0.42f;
            Sfx.PlayAt(clipName, transform.position, volume, pitch);
        }

        private Material MakeMat(Color color, float smoothness, bool unlit = false)
        {
            Material m = RuntimeMaterials.CreateLitInstance(color, smoothness, unlit);
            if (m != null) _runtimeMaterials.Add(m);
            return m;
        }

        private void SpawnDustBurst(int count)
        {
            for (int i = 0; i < count; i++)
            {
                float ang = (360f / count) * i + Random.Range(-25f, 25f);
                Vector3 vel = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * Random.Range(0.9f, 1.8f);
                vel.y = Random.Range(0.5f, 1.1f);
                SpawnDustPuff(transform.position + Vector3.up * 0.14f, vel);
            }
        }

        private void SpawnDustPuff(Vector3 worldPos, Vector3 velocity)
        {
            if (_dustPuffs.Count >= 18) return;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CatDustPuff";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * 0.10f;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = _dustMat;

            _dustPuffs.Add(new DustPuff
            {
                tr = go.transform,
                age = 0f,
                maxAge = 0.40f,
                vel = velocity
            });
        }

        private void TickDustPuffs()
        {
            for (int i = _dustPuffs.Count - 1; i >= 0; i--)
            {
                DustPuff p = _dustPuffs[i];
                if (p.tr == null)
                {
                    _dustPuffs.RemoveAt(i);
                    continue;
                }

                p.age += Time.deltaTime;
                float u = Mathf.Clamp01(p.age / p.maxAge);
                if (u >= 1f)
                {
                    Destroy(p.tr.gameObject);
                    _dustPuffs.RemoveAt(i);
                    continue;
                }

                p.tr.position += p.vel * Time.deltaTime;
                float scale = u < 0.35f
                    ? Mathf.Lerp(0.09f, 0.24f, u / 0.35f)
                    : Mathf.Lerp(0.24f, 0.01f, (u - 0.35f) / 0.65f);
                p.tr.localScale = Vector3.one * scale;
                _dustPuffs[i] = p;
            }
        }

        private void AnimateBody()
        {
            float speedXZ = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z).magnitude;
            float normSpeed = Mathf.Clamp01(speedXZ / fleeSpeed);
            float t = Time.time;

            if (_bonkSpinTimer > 0f) _bonkSpinTimer = Mathf.Max(0f, _bonkSpinTimer - Time.deltaTime);

            // Trot / gallop / frantic juke leg cycle
            float freq = _state == PetState.JukeDash
                ? 28f
                : (_state == PetState.FleeWithSock || _state == PetState.ScaredFlee ? 17f : 11f);
            float stride = _state == PetState.Taunt ? 16f : normSpeed * 40f;
            float s0 = Mathf.Sin(t * freq) * stride;
            float s1 = Mathf.Sin(t * freq + Mathf.PI) * stride;
            _legs[0].localRotation = Quaternion.Euler(s0, 0f, 0f);
            _legs[1].localRotation = Quaternion.Euler(s1, 0f, 0f);
            _legs[2].localRotation = Quaternion.Euler(s1, 0f, 0f);
            _legs[3].localRotation = Quaternion.Euler(s0, 0f, 0f);

            // Crouch squash for JukeTelegraph, dizzy spin on bonk, or bouncy trot bob
            if (_state == PetState.JukeTelegraph)
            {
                _bodyPivot.localScale = Vector3.Lerp(_bodyPivot.localScale, new Vector3(1.18f, 0.68f, 1.24f), Time.deltaTime * 24f);
                _bodyPivot.localPosition = new Vector3(0f, 0.18f, 0f);
                _bodyPivot.localRotation = Quaternion.Euler(14f, 0f, 0f);
            }
            else
            {
                _bodyPivot.localScale = Vector3.Lerp(_bodyPivot.localScale, Vector3.one, Time.deltaTime * 14f);
                float bob = _state == PetState.Taunt
                    ? Mathf.Abs(Mathf.Sin(t * 9f)) * 0.055f
                    : normSpeed * Mathf.Abs(Mathf.Sin(t * freq)) * 0.048f + Mathf.Sin(t * 3.5f) * 0.008f;
                float roll = _state == PetState.Taunt
                    ? Mathf.Sin(t * 11f) * 14f
                    : normSpeed * Mathf.Sin(t * freq * 0.5f) * 6f;
                float pitch = _state == PetState.JukeDash ? 10f : 0f;
                float spinYaw = _bonkSpinTimer > 0f ? (_bonkSpinTimer / 0.70f) * 720f : 0f;
                _bodyPivot.localPosition = new Vector3(0f, 0.24f + bob, 0f);
                _bodyPivot.localRotation = Quaternion.Euler(pitch, spinYaw, roll);
            }

            // Sassy tail wag
            float wagSpeed = _carriedSock != null ? 10.5f : 4.2f;
            float wagAmp = _carriedSock != null ? 36f : 18f;
            _tailPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * wagSpeed) * wagAmp);

            // Cheeky head tilt when carrying sock or taunting
            float headPitch = _carriedSock != null ? -14f : Mathf.Sin(t * 2.4f) * 4f;
            float headYaw = _state == PetState.Taunt
                ? Mathf.Sin(t * 11f) * 22f
                : (_carriedSock != null ? Mathf.Sin(t * 7f) * 10f : 0f);
            _headPivot.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);

            // Floating "!" marker bob + camera billboard when carrying a stolen sock
            if (_alertMarker != null && _alertMarker.activeSelf)
            {
                _alertMarker.transform.localPosition = new Vector3(0f, 0.92f + Mathf.Sin(t * 9f) * 0.08f, 0f);
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - _alertMarker.transform.position;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 0.01f)
                    {
                        _alertMarker.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                    }
                }
            }

            // Occasional cute eye blink
            if (t >= _nextBlinkTime)
            {
                _blinkEndTime = t + 0.11f;
                _nextBlinkTime = t + Random.Range(2.4f, 5.2f);
            }
            float eyeY = t < _blinkEndTime ? 0.012f : 0.068f;
            _eyeL.localScale = new Vector3(0.062f, eyeY, 0.035f);
            _eyeR.localScale = new Vector3(0.062f, eyeY, 0.035f);
        }
    }
}
