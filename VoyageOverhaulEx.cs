using UnityEngine;
using HarmonyLib;
using System.Collections.Generic;
using FMODUnity;
using System.Globalization;
using System;

public class VoyageOverhaulEx : Mod
{
    Harmony harmony;
    static public JsonModInfo modInfo;

    public void Start()
    {
        modInfo = modlistEntry.jsonmodinfo;
        harmony = new Harmony("com.metrix.VoyageOverhaulEx");
        harmony.PatchAll();
        Log("Mod has been loaded!");
    }

    public void Update()
    {
        if (ExtraSettingsAPI_Loaded)
            VoyageOverhaulSettings.Update();
    }

    public void OnModUnload()
    {
        harmony.UnpatchAll(harmony.Id);
        Log("Mod has been unloaded!");
    }

    // Extra Settings
    static public bool ExtraSettingsAPI_Loaded = false; // This is set to true while the mod's settings are loaded
    public void ExtraSettingsAPI_Load()
    {
        VoyageOverhaulSettings.Update();
    }
    public void ExtraSettingsAPI_SettingsClose()
    {
        VoyageOverhaulSettings.Update();
    }

    public static string ExtraSettingsAPI_GetInputValue(string SettingName) => "";
    public static void ExtraSettingsAPI_SetInputValue(string SettingName, string value)  { }

}

public class VoyageOverhaulSettings
{
    static public float currentSailMultiplier = 30f; // every calculated sail * multiplier (default: 30f)
    static public float currentMotorForceMultiplier = 1f; // multiplier for each motor (default: 100%)

    static public float sailMultiplier
    {
        get
        {
            if (VoyageOverhaulEx.ExtraSettingsAPI_Loaded)
                return currentSailMultiplier;

            return 30f;
        }
    }
    static public float motorForceMultiplier
    {
        get
        {
            if (VoyageOverhaulEx.ExtraSettingsAPI_Loaded)
                return currentMotorForceMultiplier;

            return 1f;
        }
    }

    public static void Update()
    {
        if (!VoyageOverhaulEx.ExtraSettingsAPI_Loaded)
            return;

        try
        {
            VoyageOverhaulSettings.currentSailMultiplier = Mathf.Max(Parse(VoyageOverhaulEx.ExtraSettingsAPI_GetInputValue("Sail Multiplier")), 1f);
        }
        catch (Exception e)
        {
            Debug.LogError($"Couldn't parse \"{VoyageOverhaulEx.ExtraSettingsAPI_GetInputValue("Sail Multiplier")}\" as float for Sail Multiplier\n{e}");
            VoyageOverhaulSettings.currentSailMultiplier = 30f;
        }

        try
        {
            VoyageOverhaulSettings.currentMotorForceMultiplier = Mathf.Max(Parse(VoyageOverhaulEx.ExtraSettingsAPI_GetInputValue("Motor Force Multiplier")), 0.01f);
        }
        catch (Exception e)
        {
            Debug.LogError($"Couldn't parse \"{VoyageOverhaulEx.ExtraSettingsAPI_GetInputValue("Motor Force Multiplier")}\" as float for Motor Force Multiplier\n{e}");
            VoyageOverhaulSettings.currentMotorForceMultiplier = 30f;
        }
    }

    static float Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 1;
        if (value.Contains(",") && !value.Contains("."))
            value = value.Replace(',', '.');

        float r = 0;
        try
        {
            r = float.Parse(value);
        } catch (Exception e)
        {
            throw e;
        }

        return r;
    }
}

[HarmonyPatch(typeof(FishingRod), "PullItemsFromSea")]
public class fishingRodPatch
{
    static AccessTools.FieldRef<FishingRod, bool> ResetRod = AccessTools.FieldRefAccess<FishingRod, bool>("ResetRod");

    [HarmonyPrefix]
    static bool pullItems(FishingRod __instance, bool ___isMetalRod, FishingBaitHandler ___fishingBaitHandler, Network_Player ___playerNetwork,
        PlayerAnimator ___playerAnimator, Rope ___rope, Throwable ___throwable)
    {
        Item_Base randomItemFromCurrentBaitPool = ___fishingBaitHandler.GetRandomItemFromCurrentBaitPool(___isMetalRod);
        if (randomItemFromCurrentBaitPool != null)
        {
            ___playerNetwork.Inventory.AddItem(randomItemFromCurrentBaitPool.UniqueName, 1);
            ___fishingBaitHandler.ConsumeBait();
        }
        ___playerAnimator.SetAnimation(PlayerAnimation.Trigger_FishingRetract, false);
        __instance.rodAnimator.SetTrigger("FishingRetract");
        ParticleManager.PlaySystem("WaterSplash_Hook", ___throwable.throwableObject.position + Vector3.up * 0.1f, true);

        ResetRod(__instance);

        if (___playerNetwork.Inventory.RemoveDurabillityFromHotSlot(1))
        {
            ___rope.gameObject.SetActive(false);
        }
        return false;
    }
}

[HarmonyPatch]
public class Patch_Raft
{
    static Dictionary<ObjectSpawner_RaftDirection, Vector3> lastSpawnPos = new Dictionary<ObjectSpawner_RaftDirection, Vector3>();
    static Rigidbody body = null;

    [HarmonyPatch(typeof(Raft), "FixedUpdate")]
    [HarmonyPrefix]
    static bool FixedUpdate(ref Raft __instance, ref Vector3 ___moveDirection, StudioEventEmitter ___eventEmitter_idle, ref Vector3 ___previousPosition, ref Vector3 ___anchorPosition, float ___maxDistanceFromAnchorPoint, Rigidbody ___body)
    {
        if (body == null)
            body = ___body;
        if (!LoadSceneManager.IsGameSceneLoaded)
            return true;
        float timeDif = Time.deltaTime;
        if (!Raft_Network.IsHost || GameModeValueManager.GetCurrentGameModeValue().raftSpecificVariables.isRaftAlwaysAnchored || (!Raft_Network.WorldHasBeenRecieved && !GameManager.IsInNewGame))
            return false;
        if (__instance.IsAnchored)
        {
            float num2 = __instance.transform.position.DistanceXZ(___anchorPosition);
            if (num2 >= ___maxDistanceFromAnchorPoint * 3f)
            {
                ___anchorPosition = __instance.transform.position;
            }
            if (num2 > ___maxDistanceFromAnchorPoint)
            {
                Vector3 vector2 = ___anchorPosition - __instance.transform.position;
                vector2.y = 0f;
                ___body.AddForce(vector2.normalized * 2f);
            }
        } else
        {
            List<Sail> allSails = Sail.AllSails;
            List<MotorWheel> allMotors = Traverse.Create<RaftVelocityManager>().Field("motors").GetValue<List<MotorWheel>>();
            Vector3 vel = Vector3.zero;
            
            // sails
            foreach (Sail sail in allSails)
                if (sail.open)
                    vel += sail.GetNormalizedDirection() * Traverse.Create(sail).Field("force").GetValue<float>();
            vel *= VoyageOverhaulSettings.sailMultiplier;

            // motors
            foreach (MotorWheel motor in allMotors)
                if (motor.MotorState)
                {
                    Vector3 motorForce = motor.PushDirection * (motor.MotorStrength + motor.ExtraMotorStrength) - new Vector3(0, 0, __instance.waterDriftSpeed);
                    vel += motorForce * VoyageOverhaulSettings.motorForceMultiplier;
                }

            // fondations and walkable blocks
            vel += new Vector3(0, 0, __instance.waterDriftSpeed * newVelocityManager.bounds.FoundationCount * 0.5f);
            vel /= newVelocityManager.bounds.WalkableBlocksCount * 0.3f;
            __instance.maxVelocity = vel.magnitude;
            ___body.velocity += vel * timeDif;

            // wheel steering rotation
            List<SteeringWheel> steeringWheels = RaftVelocityManager.steeringWheels;
            float num = 0f;
            foreach (SteeringWheel steeringWheel in steeringWheels)
            {
                num += steeringWheel.SteeringRotation;
            }
            num = Mathf.Clamp(num, -1f, 1f);
            if (num != 0f)
            {
                Vector3 torque = new Vector3(0f, Mathf.Tan(0.017453292f * num), 0f) * vel.magnitude;
                ___body.AddTorque(torque, ForceMode.Acceleration);
            }
        }
        newVelocityManager.checkRecal();
        ___body.velocity *= newVelocityManager.getDrag(__instance, timeDif);
        ___moveDirection = ___body.velocity;
        ___eventEmitter_idle.SetParameter("velocity", ___body.velocity.sqrMagnitude / __instance.maxVelocity);
        ___previousPosition = ___body.transform.position;
        return false;
    }

    [HarmonyPatch(typeof(ObjectSpawner_RaftDirection), "Update")]
    [HarmonyPrefix]
    static bool Update(ref ObjectSpawner_RaftDirection __instance, ref Vector3 ___spawnDirectionFromRaft, ref ObjectSpawnerAssetSettings ___currentSettings, Raft_Network ___network, ref float ___spawnDelay)
    {
        if (!lastSpawnPos.ContainsKey(__instance))
            lastSpawnPos.Add(__instance, Vector3.zero);
        if (!Raft_Network.IsHost || !body)
            return false;
        ___spawnDirectionFromRaft = Raft.direction.normalized;
        if (!___currentSettings.PlayerCountWithinRange(___network.PlayerCount))
            ___currentSettings = __instance.spawnSettingsAsset.GetSettings(___network.PlayerCount);
        if ((__instance.maxSpawnedObjects == -1 || __instance.spawnedObjects.Count < __instance.maxSpawnedObjects) && ___spawnDelay > 0f)
        {
            if (body.position.DistanceXZ(lastSpawnPos[__instance]) * 0.5f >= ___spawnDelay)
            {
                lastSpawnPos[__instance] = body.position;
                ___spawnDelay = ___currentSettings.spawnRateInterval.GetRandomValue();
                __instance.SpawnNewItems();
            }
        }
        if (__instance.removeItemsAutomaticly)
            Traverse.Create(__instance).Method("RemoveItemsOutsideRange", new object[0]).GetValue();
        return false;
    }

    [HarmonyPatch(typeof(Raft), "Moving", MethodType.Getter)]
    [HarmonyPrefix]
    static bool Moving(ref bool __result)
    {
        __result = true;
        return false;
    }
}

public static class newVelocityManager
{
    public static float xDrag = 0;
    public static float zDrag = 0;
    private static int preCount = 0;
    private static RaftBounds _bounds;
    public static RaftBounds bounds
    {
        get
        {
            if (_bounds == null)
                _bounds = UnityEngine.Object.FindObjectOfType<RaftBounds>();
            return _bounds;
        }
    }

    public static void RecalculateDrags()
    {
        List<Vector2> xFill = new List<Vector2>();
        List<Vector2> zFill = new List<Vector2>();
        List<Block> blocks = bounds.walkableBlocks;
        preCount = blocks.Count;
        foreach (Block block in blocks)
        {
            Vector2 vec = new Vector2((int)block.transform.localPosition.z, (int)block.transform.localPosition.y);
            if (!xFill.Contains(vec))
                xFill.Add(vec);
            vec = new Vector2((int)block.transform.localPosition.x, (int)block.transform.localPosition.y);
            if (!zFill.Contains(vec))
                zFill.Add(vec);
        }
        xDrag = (xFill.Count * 0.4f + blocks.Count * 0.6f) / blocks.Count;
        zDrag = (zFill.Count * 0.4f + blocks.Count * 0.6f) / blocks.Count;
    }

    public static void checkRecal()
    {
        if (bounds.walkableBlocks.Count != preCount)
            RecalculateDrags();
    }

    public static float getDrag(Raft raft, float timeDif)
    {
        Vector3 vel = Vector3.one;
        if (raft.VelocityDirection.magnitude != 0)
        {
            vel = raft.VelocityDirection / raft.VelocityDirection.magnitude;
        }
        float drag = new Vector3(zDrag * vel.x, 0, xDrag * vel.z).magnitude;
        return Mathf.Pow(drag,timeDif);
    }
}