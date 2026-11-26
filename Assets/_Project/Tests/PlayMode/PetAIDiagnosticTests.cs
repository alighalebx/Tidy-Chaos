using System;
using System.Collections;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace WheresMyLeftSock.Tests.PlayMode
{
    public class PetAIDiagnosticTests
    {
        private Type _gameManagerType;
        private Type _petAiType;

        [OneTimeSetUp]
        public void ResolveTypes()
        {
            _gameManagerType = Type.GetType("WheresMyLeftSock.Core.GameManager, Assembly-CSharp");
            _petAiType = Type.GetType("WheresMyLeftSock.World.PetAI, Assembly-CSharp");
            Assert.IsNotNull(_gameManagerType, "Failed to resolve GameManager in Assembly-CSharp");
            Assert.IsNotNull(_petAiType, "Failed to resolve PetAI in Assembly-CSharp");
        }

        [TearDown]
        public void RestoreTimeScale()
        {
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator ChaosCat_NavigatesAndStealsSock_AcrossThreeRuns()
        {
            for (int run = 1; run <= 3; run++)
            {
                AsyncOperation loadOp = SceneManager.LoadSceneAsync("HouseLevel", LoadSceneMode.Single);
                Assert.IsNotNull(loadOp, "Failed to start async load of HouseLevel");
                while (!loadOp.isDone) yield return null;

                // Let Awake/Start run before calling StartRun
                yield return null;
                yield return null;

                Time.timeScale = 2f;

                object gm = _gameManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                Assert.IsNotNull(gm, $"Run {run}: GameManager.Instance was null in HouseLevel.");
                _gameManagerType.GetMethod("StartRun", BindingFlags.Public | BindingFlags.Instance)?.Invoke(gm, null);

                Component pet = null;
                float spawnWait = 0f;
                while (pet == null && spawnWait < 3f)
                {
                    pet = UnityEngine.Object.FindFirstObjectByType(_petAiType) as Component;
                    if (pet != null) break;
                    spawnWait += Time.deltaTime;
                    yield return null;
                }

                // Wait two frames for PetAI.Start() navgraph build to finish
                yield return null;
                yield return null;

                Assert.IsNotNull(pet, $"Run {run}: PetAI (ChaosCat) did not spawn within 3s of StartRun().");

                string navReport = InvokeString(pet, "GetNavGraphDiagnosticReport");
                string socksReport = InvokeString(pet, "GetSocksDiagnosticReport");

                StringBuilder runLog = new StringBuilder();
                runLog.AppendLine($"================ DIAGNOSTIC RUN {run}/3 ================");
                runLog.AppendLine(navReport);
                runLog.AppendLine(socksReport);
                Debug.Log(navReport);
                Debug.Log(socksReport);

                int connectedRooms = GetProp<int>(pet, "ConnectedRoomCount");
                bool hasHall = InvokeBool(pet, "HasConnectedRoom", "Hallway");
                bool hasBed = InvokeBool(pet, "HasConnectedRoom", "Bedroom");
                bool hasLiving = InvokeBool(pet, "HasConnectedRoom", "LivingRoom");

                Vector3 prevPos = pet.transform.position;
                bool stoleSock = false;
                Vector3 pickupPos = Vector3.zero;
                float maxFleeDist = 0f;
                float totalDistMoved = 0f;

                for (int sec = 1; sec <= 35; sec++)
                {
                    yield return new WaitForSeconds(1f);

                    if (pet == null)
                    {
                        runLog.AppendLine($"  t={sec}s: PET DESTROYED");
                        break;
                    }

                    Vector3 curPos = pet.transform.position;
                    float stepXZ = Vector2.Distance(new Vector2(prevPos.x, prevPos.z), new Vector2(curPos.x, curPos.z));
                    totalDistMoved += stepXZ;
                    prevPos = curPos;

                    string stateName = GetProp<string>(pet, "CurrentStateName");
                    string targetSock = GetProp<string>(pet, "TargetSockName");
                    string carriedSock = GetProp<string>(pet, "CarriedSockName");
                    bool carrying = GetProp<bool>(pet, "IsCarryingSock");
                    int softStuck = GetProp<int>(pet, "SoftStuckCount");
                    int hardStuck = GetProp<int>(pet, "HardStuckCount");
                    int toasts = GetProp<int>(pet, "StealToastCount");

                    if (carrying)
                    {
                        if (!stoleSock)
                        {
                            stoleSock = true;
                            pickupPos = curPos;
                        }
                        float fleeXZ = Vector2.Distance(new Vector2(pickupPos.x, pickupPos.z), new Vector2(curPos.x, curPos.z));
                        if (fleeXZ > maxFleeDist) maxFleeDist = fleeXZ;
                    }

                    string sampleLine = $"  t={sec,2}s | state={stateName,-13} | pos=({curPos.x:F2},{curPos.y:F2},{curPos.z:F2}) | dXZ={stepXZ:F2}m | target='{targetSock}' | carried='{carriedSock}' | softStuck={softStuck} hardStuck={hardStuck} toasts={toasts}";
                    runLog.AppendLine(sampleLine);
                    Debug.Log(sampleLine);
                }

                int finalSoft = GetProp<int>(pet, "SoftStuckCount");
                int finalHard = GetProp<int>(pet, "HardStuckCount");
                int finalToasts = GetProp<int>(pet, "StealToastCount");
                int totalStuck = finalSoft + finalHard;

                string summary = runLog.ToString();

                Assert.GreaterOrEqual(connectedRooms, 4, $"Run {run}: Expected >= 4 connected rooms.\n{summary}");
                Assert.IsTrue(hasHall && hasBed && hasLiving, $"Run {run}: Expected Hallway, Bedroom, and LivingRoom in nav graph.\n{summary}");
                Assert.LessOrEqual(totalStuck, 1, $"Run {run}: Cat triggered stuck recovery {totalStuck} times (soft={finalSoft}, hard={finalHard}).\n{summary}");
                Assert.GreaterOrEqual(totalDistMoved, 6f, $"Run {run}: Cat barely moved ({totalDistMoved:F2}m).\n{summary}");
                Assert.IsTrue(stoleSock, $"Run {run}: Cat never picked up any sock within 35s.\n{summary}");
                Assert.GreaterOrEqual(finalToasts, 1, $"Run {run}: Steal toast did not fire.\n{summary}");
                Assert.GreaterOrEqual(maxFleeDist, 4f, $"Run {run}: Cat only fled {maxFleeDist:F2}m (< 4m) from pickup.\n{summary}");
            }
        }

        private static T GetProp<T>(object target, string propName)
        {
            PropertyInfo prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(prop, $"Missing property {propName} on {target.GetType().Name}");
            return (T)prop.GetValue(target);
        }

        private static string InvokeString(object target, string methodName)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(m, $"Missing method {methodName} on {target.GetType().Name}");
            return (string)m.Invoke(target, null);
        }

        private static bool InvokeBool(object target, string methodName, string arg)
        {
            MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(m, $"Missing method {methodName} on {target.GetType().Name}");
            return (bool)m.Invoke(target, new object[] { arg });
        }
    }
}
