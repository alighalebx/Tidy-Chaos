using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace WheresMyLeftSock.Tests.PlayMode
{
    public class CameraPolishTests
    {
        private Type _gameManagerType;
        private Type _runIntroType;
        private Type _camType;
        private Type _playerControllerType;
        private Type _goofyBodyType;
        private Type _rideCarType;

        [OneTimeSetUp]
        public void ResolveTypes()
        {
            _gameManagerType = Type.GetType("WheresMyLeftSock.Core.GameManager, Assembly-CSharp");
            _runIntroType = Type.GetType("WheresMyLeftSock.Core.RunIntro, Assembly-CSharp");
            _camType = Type.GetType("WheresMyLeftSock.Player.ThirdPersonCamera, Assembly-CSharp");
            _playerControllerType = Type.GetType("WheresMyLeftSock.Player.PlayerController, Assembly-CSharp");
            _goofyBodyType = Type.GetType("WheresMyLeftSock.Player.GoofyBody, Assembly-CSharp");
            _rideCarType = Type.GetType("WheresMyLeftSock.World.RideCar, Assembly-CSharp");

            Assert.IsNotNull(_gameManagerType, "Failed to resolve GameManager");
            Assert.IsNotNull(_runIntroType, "Failed to resolve RunIntro");
            Assert.IsNotNull(_camType, "Failed to resolve ThirdPersonCamera");
            Assert.IsNotNull(_playerControllerType, "Failed to resolve PlayerController");
            Assert.IsNotNull(_goofyBodyType, "Failed to resolve GoofyBody");
            Assert.IsNotNull(_rideCarType, "Failed to resolve RideCar");
        }

        [TearDown]
        public void Cleanup()
        {
            Time.timeScale = 1f;
            SetHasPlayedOnce(false);
        }

        [UnityTest]
        public IEnumerator CameraPolish_IntroSwoopWinLoseAndRestart()
        {
            // 1. Fresh session Menu -> IntroCar framing RideCar outside
            SetHasPlayedOnce(false);
            AsyncOperation loadOp = SceneManager.LoadSceneAsync("HouseLevel", LoadSceneMode.Single);
            Assert.IsNotNull(loadOp);
            while (!loadOp.isDone) yield return null;
            yield return null;
            yield return null;
            yield return null;

            object gm = GetGameManager();
            Assert.IsNotNull(gm, "GameManager.Instance missing after load.");
            Assert.AreEqual("Menu", GetProp<object>(gm, "CurrentState").ToString(), "Expected Menu state on fresh session.");

            Component camRig = UnityEngine.Object.FindFirstObjectByType(_camType) as Component;
            Component player = UnityEngine.Object.FindFirstObjectByType(_playerControllerType) as Component;
            Component body = UnityEngine.Object.FindFirstObjectByType(_goofyBodyType) as Component;
            Component car = UnityEngine.Object.FindFirstObjectByType(_rideCarType) as Component;
            Assert.IsNotNull(camRig, "ThirdPersonCamera missing.");
            Assert.IsNotNull(player, "PlayerController missing.");
            Assert.IsNotNull(body, "GoofyBody missing.");
            Assert.IsNotNull(car, "RideCar missing.");

            Camera unityCam = GetProp<Camera>(camRig, "Camera");
            Assert.IsNotNull(unityCam, "Unity Camera reference missing.");
            Assert.AreEqual("IntroCar", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected IntroCar mode in Menu.");

            Vector3 carLook = car.transform.position + Vector3.up * 0.85f;
            AssertClearSightline(unityCam.transform.position, carLook, car.transform, null, "IntroCar -> RideCar");
            Vector3 carVp = unityCam.WorldToViewportPoint(carLook);
            Assert.Greater(carVp.z, 0f, "RideCar is behind IntroCar camera.");
            Assert.That(carVp.x, Is.InRange(0.05f, 0.95f), $"RideCar viewport X out of frame: {carVp.x:F2}");
            Assert.That(carVp.y, Is.InRange(0.05f, 0.95f), $"RideCar viewport Y out of frame: {carVp.y:F2}");

            // 2. StartRun -> 0.75s IntroSwoop -> Gameplay
            SetHasPlayedOnce(true);
            InvokeMethod(gm, "StartRun");
            yield return null;

            Assert.AreEqual("IntroSwoop", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected IntroSwoop immediately after StartRun.");
            Assert.IsTrue(GetProp<bool>(gm, "IsIntroSwoopActive"), "Expected IsIntroSwoopActive == true during swoop.");
            Assert.IsFalse(GetProp<bool>(player, "ControlsEnabled"), "Player movement should stay gated during IntroSwoop.");
            Assert.IsFalse(GetProp<bool>(camRig, "IsTargetHidden"), "GoofyBody must remain visible during IntroSwoop.");
            AssertAllBodyRenderersVisible(body, "IntroSwoop");

            yield return new WaitForSeconds(1.05f);

            Assert.AreEqual("Gameplay", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected Gameplay mode after swoop finishes.");
            Assert.IsFalse(GetProp<bool>(gm, "IsIntroSwoopActive"), "IsIntroSwoopActive should clear after swoop.");
            Assert.IsTrue(GetProp<bool>(player, "ControlsEnabled"), "Player controls must unlock after swoop.");
            Vector3 toPlayer = (player.transform.position + Vector3.up * 1.1f - unityCam.transform.position).normalized;
            float camFacingDot = Vector3.Dot(toPlayer, unityCam.transform.forward);
            Assert.Greater(camFacingDot, 0.7f, $"Expected camera behind player facing forward (dot={camFacingDot:F2}).");

            // 3. Win near FrontDoor -> WinShowcase (hero left-third + right-side card)
            player.transform.position = new Vector3(5.25f, 0.16f, 1.35f);
            Physics.SyncTransforms();
            InvokeMethod(gm, "TriggerWin");
            yield return new WaitForSeconds(0.75f);

            Assert.AreEqual("WinShowcase", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected WinShowcase on Win.");
            Assert.IsFalse(GetProp<bool>(player, "ControlsEnabled"), "Controls must disable on Win.");
            Assert.IsFalse(GetProp<bool>(camRig, "IsTargetHidden"), "GoofyBody must not hide during WinShowcase.");
            Assert.AreEqual("Celebrate", GetProp<object>(body, "PoseMode").ToString(), "GoofyBody should enter Celebrate pose on Win.");
            AssertAllBodyRenderersVisible(body, "WinShowcase");

            Vector3 playerChest = player.transform.position + Vector3.up * 0.92f;
            AssertClearSightline(unityCam.transform.position, playerChest, player.transform, null, "WinShowcase -> Player");
            Vector3 playerVp = unityCam.WorldToViewportPoint(playerChest);
            Assert.Greater(playerVp.z, 0f, "Player is behind WinShowcase camera.");
            Assert.That(playerVp.x, Is.InRange(0.08f, 0.55f), $"Expected player framed in left half/third on WinShowcase (vp.x={playerVp.x:F2}).");

            GameObject hud = GameObject.Find("HUDCanvas");
            Transform endPanel = hud != null ? hud.transform.Find("EndScreenPanel") : null;
            Assert.IsNotNull(endPanel, "EndScreenPanel missing.");
            Assert.IsTrue(endPanel.gameObject.activeInHierarchy, "EndScreenPanel should be active on Win.");
            Image backdrop = endPanel.GetComponent<Image>();
            Assert.IsNotNull(backdrop);
            Assert.LessOrEqual(backdrop.color.a, 0.25f, $"EndScreenPanel backdrop should be subtle (alpha={backdrop.color.a:F2}).");
            Assert.IsNotNull(endPanel.Find("ShowcaseCardBg"), "Right-side ShowcaseCardBg should be created.");

            // 4. Reload, play past swoop, Lose -> LoseShowcase, then RestartRun resets cleanly
            SetHasPlayedOnce(true);
            loadOp = SceneManager.LoadSceneAsync("HouseLevel", LoadSceneMode.Single);
            while (!loadOp.isDone) yield return null;
            yield return null;
            yield return null;

            gm = GetGameManager();
            camRig = UnityEngine.Object.FindFirstObjectByType(_camType) as Component;
            car = UnityEngine.Object.FindFirstObjectByType(_rideCarType) as Component;
            unityCam = GetProp<Camera>(camRig, "Camera");

            // Wait for 0.75s swoop from RunIntro.HasPlayedOnce StartRun
            yield return new WaitForSeconds(0.95f);
            Assert.AreEqual("Gameplay", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected Gameplay after post-restart swoop.");

            Vector3 carStartPos = car.transform.position;
            InvokeMethod(gm, "TriggerLose");
            yield return new WaitForSeconds(0.55f);

            Assert.AreEqual("LoseShowcase", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected LoseShowcase on Lose.");
            Vector3 fleeingCarLook = car.transform.position + Vector3.up * 0.85f;
            AssertClearSightline(unityCam.transform.position, fleeingCarLook, car.transform, null, "LoseShowcase -> RideCar");
            Assert.Greater(Vector3.Distance(carStartPos, car.transform.position), 0.3f, "RideCar should peel away on Lose.");
            hud = GameObject.Find("HUDCanvas");
            endPanel = hud != null ? hud.transform.Find("EndScreenPanel") : null;
            Assert.IsTrue(endPanel != null && endPanel.gameObject.activeInHierarchy, "EndScreenPanel should be active on Lose.");

            // RestartRun must reload cleanly back into working state
            InvokeMethod(gm, "RestartRun");
            yield return null;
            yield return null;
            yield return new WaitForSeconds(1.1f);

            gm = GetGameManager();
            Assert.IsNotNull(gm, "GameManager missing after RestartRun.");
            camRig = UnityEngine.Object.FindFirstObjectByType(_camType) as Component;
            body = UnityEngine.Object.FindFirstObjectByType(_goofyBodyType) as Component;
            player = UnityEngine.Object.FindFirstObjectByType(_playerControllerType) as Component;
            hud = GameObject.Find("HUDCanvas");
            endPanel = hud != null ? hud.transform.Find("EndScreenPanel") : null;

            Assert.IsTrue(endPanel == null || !endPanel.gameObject.activeSelf, "EndScreenPanel should be hidden after RestartRun.");
            AssertAllBodyRenderersVisible(body, "PostRestart");
            Assert.AreEqual("Gameplay", GetProp<object>(camRig, "CurrentMode").ToString(), "Expected Gameplay mode after RestartRun swoop.");
            Assert.IsTrue(GetProp<bool>(player, "ControlsEnabled"), "Player controls should be enabled after RestartRun swoop.");
        }

        private object GetGameManager()
        {
            return _gameManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        private void SetHasPlayedOnce(bool value)
        {
            FieldInfo f = _runIntroType.GetField("HasPlayedOnce", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(f);
            f.SetValue(null, value);
        }

        private static T GetProp<T>(object target, string propName)
        {
            PropertyInfo p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(p, $"Missing property {propName} on {target.GetType().Name}");
            return (T)p.GetValue(target);
        }

        private static void InvokeMethod(object target, string methodName)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(m, $"Missing method {methodName} on {target.GetType().Name}");
            m.Invoke(target, null);
        }

        private static void AssertAllBodyRenderersVisible(Component body, string context)
        {
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, $"{context}: Expected GoofyBody renderers.");
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.activeInHierarchy)
                {
                    Assert.IsTrue(renderers[i].enabled, $"{context}: Renderer '{renderers[i].name}' was disabled.");
                }
            }
        }

        private static void AssertClearSightline(Vector3 from, Vector3 to, Transform ignoreA, Transform ignoreB, string label)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.05f) return;

            RaycastHit[] hits = Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;
                if (ignoreA != null && c.transform.IsChildOf(ignoreA)) continue;
                if (ignoreB != null && c.transform.IsChildOf(ignoreB)) continue;
                Rigidbody rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                Assert.Fail($"{label}: Sightline blocked by '{c.name}' at {hits[i].distance:F2}m (total {dist:F2}m).");
            }
        }
    }
}
