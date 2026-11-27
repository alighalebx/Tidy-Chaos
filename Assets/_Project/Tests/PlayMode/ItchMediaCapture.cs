using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WheresMyLeftSock.Tests.PlayMode
{
    public class ItchMediaCapture
    {
        private Type _gameManagerType;
        private Type _runIntroType;
        private Type _gameUIType;
        private Type _camType;
        private Type _playerControllerType;
        private Type _petAiType;
        private Type _wearableItemType;
        private Type _itemDataType;
        private Type _openableType;
        private Type _clutterType;

        [OneTimeSetUp]
        public void ResolveTypes()
        {
            _gameManagerType = Type.GetType("WheresMyLeftSock.Core.GameManager, Assembly-CSharp");
            _runIntroType = Type.GetType("WheresMyLeftSock.Core.RunIntro, Assembly-CSharp");
            _gameUIType = Type.GetType("WheresMyLeftSock.UI.GameUI, Assembly-CSharp");
            _camType = Type.GetType("WheresMyLeftSock.Player.ThirdPersonCamera, Assembly-CSharp");
            _playerControllerType = Type.GetType("WheresMyLeftSock.Player.PlayerController, Assembly-CSharp");
            _petAiType = Type.GetType("WheresMyLeftSock.World.PetAI, Assembly-CSharp");
            _wearableItemType = Type.GetType("WheresMyLeftSock.Items.WearableItem, Assembly-CSharp");
            _itemDataType = Type.GetType("WheresMyLeftSock.Items.ItemData, Assembly-CSharp");
            _openableType = Type.GetType("WheresMyLeftSock.World.OpenableFurniture, Assembly-CSharp");
            _clutterType = Type.GetType("WheresMyLeftSock.World.PhysicsClutter, Assembly-CSharp");

            Assert.IsNotNull(_gameManagerType);
            Assert.IsNotNull(_runIntroType);
            Assert.IsNotNull(_gameUIType);
            Assert.IsNotNull(_camType);
            Assert.IsNotNull(_playerControllerType);
            Assert.IsNotNull(_petAiType);
            Assert.IsNotNull(_wearableItemType);
            Assert.IsNotNull(_itemDataType);
            Assert.IsNotNull(_openableType);
            Assert.IsNotNull(_clutterType);
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = 1f;
            SetHasPlayedOnce(false);
        }

        [UnityTest]
        public IEnumerator CaptureAllItchMedia()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string marketingDir = Path.Combine(projectRoot, "Marketing");
            string wakeupFramesDir = Path.Combine(marketingDir, "_frames_wakeup");
            string winFramesDir = Path.Combine(marketingDir, "_frames_win");
            Directory.CreateDirectory(marketingDir);
            Directory.CreateDirectory(wakeupFramesDir);
            Directory.CreateDirectory(winFramesDir);

            // 1. IntroCar Title Screen + Wake-Up Swoop GIF frames
            SetHasPlayedOnce(false);
            AsyncOperation loadOp = SceneManager.LoadSceneAsync("HouseLevel", LoadSceneMode.Single);
            while (!loadOp.isDone) yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.45f);

            object gm = _gameManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object gameUI = _gameUIType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            Component camRig = UnityEngine.Object.FindFirstObjectByType(_camType) as Component;
            Component player = UnityEngine.Object.FindFirstObjectByType(_playerControllerType) as Component;
            Camera cam = GetProp<Camera>(camRig, "Camera");
            Assert.IsNotNull(gm);
            Assert.IsNotNull(camRig);
            Assert.IsNotNull(player);
            Assert.IsNotNull(cam);

            yield return new WaitForEndOfFrame();
            CaptureFrame(cam, 1920, 1080, Path.Combine(marketingDir, "01_title_car.png"));

            int frameIdx = 0;
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame(cam, 640, 360, Path.Combine(wakeupFramesDir, $"frame_{frameIdx++:000}.png"));
                yield return new WaitForSecondsRealtime(1f / 15f);
            }

            SetHasPlayedOnce(true);
            if (gameUI != null)
            {
                _gameUIType.GetMethod("ShowTitle", BindingFlags.Public | BindingFlags.Instance)?.Invoke(gameUI, new object[] { false });
            }
            InvokeMethod(gm, "StartRun");

            for (int i = 0; i < 35; i++)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame(cam, 640, 360, Path.Combine(wakeupFramesDir, $"frame_{frameIdx++:000}.png"));
                yield return new WaitForSecondsRealtime(1f / 15f);
            }

            // 2. Bedroom Chaos Action Shot (open drawers + tossed clutter)
            player.transform.SetPositionAndRotation(new Vector3(4.9f, 0.16f, 12.8f), Quaternion.Euler(0f, -35f, 0f));
            Rigidbody playerRb = player.GetComponent<Rigidbody>();
            if (playerRb != null)
            {
                playerRb.position = player.transform.position;
                playerRb.rotation = player.transform.rotation;
                playerRb.linearVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();

            MethodInfo setYawPitch = _camType.GetMethod("SetYawPitch", BindingFlags.Public | BindingFlags.Instance);
            setYawPitch?.Invoke(camRig, new object[] { -35f, 16f });

            UnityEngine.Object[] openables = UnityEngine.Object.FindObjectsByType(_openableType, FindObjectsSortMode.None);
            for (int i = 0; i < openables.Length; i++)
            {
                Component c = openables[i] as Component;
                if (c != null && Vector3.Distance(c.transform.position, player.transform.position) <= 5.5f)
                {
                    bool isOpen = GetProp<bool>(c, "IsOpen");
                    if (!isOpen) InvokeMethod(c, "Interact", new object[] { player.gameObject });
                }
            }

            UnityEngine.Object[] clutters = UnityEngine.Object.FindObjectsByType(_clutterType, FindObjectsSortMode.None);
            int tossed = 0;
            for (int i = 0; i < clutters.Length && tossed < 8; i++)
            {
                Component c = clutters[i] as Component;
                if (c == null) continue;
                float d = Vector3.Distance(c.transform.position, new Vector3(4.2f, 0.2f, 13.8f));
                if (d <= 4.5f)
                {
                    Rigidbody crb = c.GetComponent<Rigidbody>();
                    if (crb != null && !crb.isKinematic)
                    {
                        crb.linearVelocity = new Vector3(UnityEngine.Random.Range(-1.8f, 1.8f), UnityEngine.Random.Range(2.0f, 3.6f), UnityEngine.Random.Range(-1.8f, 1.8f));
                        crb.angularVelocity = UnityEngine.Random.onUnitSphere * 6f;
                        tossed++;
                    }
                }
            }

            yield return new WaitForSeconds(0.24f);
            yield return new WaitForEndOfFrame();
            CaptureFrame(cam, 1920, 1080, Path.Combine(marketingDir, "02_bedroom_chaos.png"));

            // 3. Cat Heist Shot (ChaosCat running in Living Room with stolen sock + phone toast)
            Component pet = UnityEngine.Object.FindFirstObjectByType(_petAiType) as Component;
            Assert.IsNotNull(pet, "ChaosCat PetAI missing");

            Component chosenSock = FindUncollectedSock();
            Assert.IsNotNull(chosenSock, "No uncollected sock found for Cat Heist shot.");

            Vector3 catSpot = new Vector3(3.8f, 0.16f, 3.4f);
            Quaternion catRot = Quaternion.LookRotation(new Vector3(0.92f, 0f, -0.38f).normalized, Vector3.up);
            Rigidbody petRb = pet.GetComponent<Rigidbody>();
            pet.transform.SetPositionAndRotation(catSpot, catRot);
            if (petRb != null)
            {
                petRb.position = catSpot;
                petRb.rotation = catRot;
                petRb.linearVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();

            FieldInfo targetSockField = _petAiType.GetField("_targetSock", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo stealMethod = _petAiType.GetMethod("StealTargetSock", BindingFlags.NonPublic | BindingFlags.Instance);
            targetSockField?.SetValue(pet, chosenSock);
            stealMethod?.Invoke(pet, null);

            yield return new WaitForSeconds(0.42f);

            Behaviour camRigBehaviour = camRig as Behaviour;
            if (camRigBehaviour != null) camRigBehaviour.enabled = false;
            MonoBehaviour hudComp = UnityEngine.Object.FindFirstObjectByType(Type.GetType("WheresMyLeftSock.UI.Phase1DebugHUD, Assembly-CSharp")) as MonoBehaviour;
            if (hudComp != null)
            {
                Transform cross = hudComp.transform.Find("Crosshair");
                if (cross != null) cross.gameObject.SetActive(false);
            }
            GameObject hoverObj = GameObject.Find("HoverPromptText");
            if (hoverObj != null) hoverObj.SetActive(false);

            Vector3 pPos = pet.transform.position;
            Vector3 pFwd = Vector3.ProjectOnPlane(pet.transform.forward, Vector3.up).normalized;
            if (pFwd.sqrMagnitude < 0.01f) pFwd = Vector3.right;
            Vector3 pRight = Vector3.Cross(Vector3.up, pFwd).normalized;
            Vector3 catFocus = pPos + Vector3.up * 0.36f;
            Vector3 desiredCatCam = pPos + pFwd * 1.45f + pRight * 1.05f + Vector3.up * 0.62f;

            // Pull closer if static furniture sits on the sightline
            Vector3 toCatCam = desiredCatCam - catFocus;
            RaycastHit[] sightHits = Physics.RaycastAll(catFocus, toCatCam.normalized, toCatCam.magnitude, ~0, QueryTriggerInteraction.Ignore);
            float clearDist = toCatCam.magnitude;
            for (int i = 0; i < sightHits.Length; i++)
            {
                Collider col = sightHits[i].collider;
                if (col == null || col.transform.IsChildOf(pet.transform) || (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic))
                    continue;
                clearDist = Mathf.Min(clearDist, Mathf.Max(0.85f, sightHits[i].distance - 0.12f));
            }
            desiredCatCam = catFocus + toCatCam.normalized * clearDist;

            float prevFov = cam.fieldOfView;
            cam.transform.SetPositionAndRotation(desiredCatCam, Quaternion.LookRotation((catFocus - desiredCatCam).normalized, Vector3.up));
            cam.fieldOfView = 50f;
            yield return new WaitForEndOfFrame();
            CaptureFrame(cam, 1920, 1080, Path.Combine(marketingDir, "03_cat_heist.png"));

            cam.fieldOfView = prevFov;
            if (camRigBehaviour != null) camRigBehaviour.enabled = true;

            // 4. Reload cleanly so no linger toast overlaps end card, equip chaotic mismatched outfit, & capture WinShowcase
            SetHasPlayedOnce(true);
            loadOp = SceneManager.LoadSceneAsync("HouseLevel", LoadSceneMode.Single);
            while (!loadOp.isDone) yield return null;
            yield return null;
            yield return null;
            yield return new WaitForSeconds(0.95f);

            gm = _gameManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            player = UnityEngine.Object.FindFirstObjectByType(_playerControllerType) as Component;
            pet = UnityEngine.Object.FindFirstObjectByType(_petAiType) as Component;
            camRig = UnityEngine.Object.FindFirstObjectByType(_camType) as Component;
            cam = GetProp<Camera>(camRig, "Camera");
            playerRb = player.GetComponent<Rigidbody>();

            // Keep ChaosCat tucked in the back hallway so it does not photobomb the hero portrait
            if (pet != null)
            {
                Rigidbody prb = pet.GetComponent<Rigidbody>();
                pet.transform.position = new Vector3(3.0f, 0.16f, 13.0f);
                if (prb != null) { prb.position = pet.transform.position; prb.linearVelocity = Vector3.zero; }
                Physics.SyncTransforms();
            }

            EquipMismatchedOutfit(player.gameObject);
            yield return new WaitForSeconds(0.08f);

            Vector3 winSpot = new Vector3(5.25f, 0.16f, 2.05f);
            player.transform.SetPositionAndRotation(winSpot, Quaternion.Euler(0f, 180f, 0f));
            if (playerRb != null)
            {
                playerRb.position = winSpot;
                playerRb.rotation = Quaternion.Euler(0f, 180f, 0f);
                playerRb.linearVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();

            InvokeMethod(gm, "TriggerWin");
            yield return new WaitForSecondsRealtime(0.80f);
            yield return new WaitForEndOfFrame();
            CaptureFrame(cam, 1920, 1080, Path.Combine(marketingDir, "04_dignity_showcase.png"));

            for (int i = 0; i < 52; i++)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame(cam, 640, 360, Path.Combine(winFramesDir, $"frame_{i:000}.png"));
                yield return new WaitForSecondsRealtime(1f / 15f);
            }
        }

        private Component FindUncollectedSock()
        {
            UnityEngine.Object[] items = UnityEngine.Object.FindObjectsByType(_wearableItemType, FindObjectsSortMode.None);
            Component fallback = null;
            for (int i = 0; i < items.Length; i++)
            {
                Component w = items[i] as Component;
                if (w == null || GetProp<bool>(w, "IsCollected") || GetProp<bool>(w, "IsCarriedByPet")) continue;
                object data = _wearableItemType.GetField("data")?.GetValue(w);
                if (data == null) continue;
                string slot = _itemDataType.GetField("slot")?.GetValue(data)?.ToString();
                if (slot != "LeftFoot" && slot != "RightFoot") continue;
                bool isTrap = (bool)(_itemDataType.GetProperty("IsTrap")?.GetValue(data) ?? false);
                if (!isTrap) return w;
                fallback ??= w;
            }
            return fallback;
        }

        private void EquipMismatchedOutfit(GameObject playerGo)
        {
            UnityEngine.Object[] items = UnityEngine.Object.FindObjectsByType(_wearableItemType, FindObjectsSortMode.None);
            string[] wantedSlots = { "Legs", "LeftFoot", "RightFoot", "Torso", "Accessory" };
            bool[] preferTrap = { true, false, true, false, true };

            for (int s = 0; s < wantedSlots.Length; s++)
            {
                Component best = null;
                Component anySlot = null;
                for (int i = 0; i < items.Length; i++)
                {
                    Component w = items[i] as Component;
                    if (w == null || GetProp<bool>(w, "IsCollected") || GetProp<bool>(w, "IsCarriedByPet")) continue;
                    object data = _wearableItemType.GetField("data")?.GetValue(w);
                    if (data == null) continue;
                    string slot = _itemDataType.GetField("slot")?.GetValue(data)?.ToString();
                    if (slot != wantedSlots[s]) continue;
                    anySlot ??= w;
                    bool isTrap = (bool)(_itemDataType.GetProperty("IsTrap")?.GetValue(data) ?? false);
                    if (isTrap == preferTrap[s])
                    {
                        best = w;
                        break;
                    }
                }
                Component pick = best ?? anySlot;
                if (pick != null) InvokeMethod(pick, "Interact", new object[] { playerGo });
            }
        }

        private static void CaptureFrame(Camera cam, int width, int height, string filePath)
        {
            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            List<Canvas> switched = new List<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] != null && canvases[i].isActiveAndEnabled && canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                    canvases[i].worldCamera = cam;
                    canvases[i].planeDistance = 0.25f;
                    switched.Add(canvases[i]);
                }
            }
            Canvas.ForceUpdateCanvases();

            float prevNear = cam.nearClipPlane;
            cam.nearClipPlane = 0.08f;

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 4;
            RenderTexture prevActive = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            cam.nearClipPlane = prevNear;
            RenderTexture.active = prevActive;

            for (int i = 0; i < switched.Count; i++)
            {
                if (switched[i] != null)
                {
                    switched[i].renderMode = RenderMode.ScreenSpaceOverlay;
                    switched[i].worldCamera = null;
                }
            }
            Canvas.ForceUpdateCanvases();

            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(filePath, png);

            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        private void SetHasPlayedOnce(bool value)
        {
            _runIntroType.GetField("HasPlayedOnce", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, value);
        }

        private static T GetProp<T>(object target, string propName)
        {
            PropertyInfo p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(p, $"Missing property {propName}");
            return (T)p.GetValue(target);
        }

        private static void InvokeMethod(object target, string methodName, object[] args = null)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(m, $"Missing method {methodName}");
            m.Invoke(target, args);
        }
    }
}
