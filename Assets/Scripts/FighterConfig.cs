using System;
using UnityEngine;

public enum AttackType { None, Light, Heavy, Low, Super }

// Mid: block standing or crouching. High (overhead): block standing. Low: block crouching.
public enum GuardType { Mid, High, Low }

[Serializable]
public class AttackData
{
    public string name;
    public AttackType type;
    public GuardType guard;

    [Header("Frame data (60 fps)")]
    public int startup;
    public int active;
    public int recovery;

    [Header("On connect")]
    public int damage;
    public int hitstun;
    public int blockstun;
    public int hitstop;
    public float pushbackHit;
    public float pushbackBlock;
    [Tooltip("Flux gained when this attack connects and is a different type than your previous connected attack.")]
    public float fluxGain;
    [Tooltip("Launches a grounded opponent into a knockdown.")]
    public bool knockdown;

    [Header("Hitbox, relative to feet, facing right")]
    public Vector2 hitboxOffset;
    public Vector2 hitboxSize;

    public int TotalFrames => startup + active + recovery;
    public bool IsActiveFrame(int frame) => frame > startup && frame <= startup + active;

    public AttackData() { }

    public AttackData(string name, AttackType type, GuardType guard, int startup, int active, int recovery,
        int damage, int hitstun, int blockstun, int hitstop, float pushbackHit, float pushbackBlock,
        float fluxGain, Vector2 hitboxOffset, Vector2 hitboxSize, bool knockdown = false)
    {
        this.name = name;
        this.type = type;
        this.guard = guard;
        this.startup = startup;
        this.active = active;
        this.recovery = recovery;
        this.damage = damage;
        this.hitstun = hitstun;
        this.blockstun = blockstun;
        this.hitstop = hitstop;
        this.pushbackHit = pushbackHit;
        this.pushbackBlock = pushbackBlock;
        this.fluxGain = fluxGain;
        this.hitboxOffset = hitboxOffset;
        this.hitboxSize = hitboxSize;
        this.knockdown = knockdown;
    }

    public static Color TypeColor(AttackType t)
    {
        switch (t)
        {
            case AttackType.Light: return new Color(1f, 0.9f, 0.25f);
            case AttackType.Heavy: return new Color(1f, 0.45f, 0.15f);
            case AttackType.Low: return new Color(0.25f, 0.85f, 1f);
            case AttackType.Super: return new Color(1f, 0.25f, 0.9f);
            default: return Color.white;
        }
    }
}

// All tuning for the (shared) fighter. Edits made in play mode persist, so tune live.
[CreateAssetMenu(fileName = "FighterConfig", menuName = "Fighter Demo/Fighter Config")]
public class FighterConfig : ScriptableObject
{
    [Header("Health & Flux")]
    public int maxHealth = 1000;
    public float maxFlux = 100f;
    [Range(0f, 1f)] public float blockedFluxMultiplier = 0.5f;
    public bool fluxCarriesOverBetweenRounds = true;

    [Header("Movement (units per second)")]
    public float walkForwardSpeed = 4.2f;
    public float walkBackSpeed = 3.4f;
    public float jumpVelocity = 14f;
    public float gravity = 42f;
    public float jumpHorizontalSpeed = 4.8f;
    public int jumpSquatFrames = 3;
    public int landingRecoveryFrames = 4;

    [Header("Body")]
    public float bodyHalfWidth = 0.4f;
    public float standHeight = 2.0f;
    public float crouchHeight = 1.3f;

    [Header("Hit reactions")]
    public int knockdownFrames = 45;
    public float launchVelocity = 8f;
    public float launchKnockback = 3.5f;
    public int inputBufferFrames = 6;

    [Header("Combo damage scaling")]
    public int scalingStartsAtHit = 3;
    public float scalingPerHit = 0.1f;
    public float minScaling = 0.4f;

    [Header("Ground normals")]
    public AttackData light = new AttackData("Light", AttackType.Light, GuardType.Mid,
        4, 3, 8, 40, 16, 11, 6, 0.35f, 0.55f, 8f, new Vector2(0.75f, 1.45f), new Vector2(0.9f, 0.9f));
    public AttackData heavy = new AttackData("Heavy (overhead)", AttackType.Heavy, GuardType.High,
        14, 4, 18, 110, 22, 16, 11, 0.9f, 1.0f, 12f, new Vector2(0.95f, 1.25f), new Vector2(1.3f, 0.8f));
    public AttackData low = new AttackData("Low", AttackType.Low, GuardType.Low,
        7, 3, 12, 60, 19, 12, 8, 0.45f, 0.6f, 10f, new Vector2(0.85f, 0.25f), new Vector2(1.2f, 0.4f));

    [Header("Air normals (all overheads)")]
    public AttackData airLight = new AttackData("Air Light", AttackType.Light, GuardType.High,
        5, 6, 8, 45, 17, 12, 7, 0.3f, 0.4f, 8f, new Vector2(0.6f, 0.6f), new Vector2(0.9f, 0.6f));
    public AttackData airHeavy = new AttackData("Air Heavy", AttackType.Heavy, GuardType.High,
        8, 6, 12, 90, 20, 14, 10, 0.5f, 0.6f, 12f, new Vector2(0.7f, 0.5f), new Vector2(1.1f, 0.8f));
    public AttackData airLow = new AttackData("Air Low (stomp)", AttackType.Low, GuardType.High,
        6, 8, 10, 70, 19, 13, 8, 0.4f, 0.5f, 10f, new Vector2(0.35f, 0.05f), new Vector2(0.9f, 0.6f));

    [Header("Rush super")]
    public int superFreezeFrames = 30;
    public int superStartupFrames = 6;
    public int superInvulnFrames = 6;
    public int superDashFrames = 22;
    public float superDashSpeed = 16f;
    public Vector2 superHitboxOffset = new Vector2(0.55f, 1.0f);
    public Vector2 superHitboxSize = new Vector2(0.7f, 1.6f);
    public int superHits = 5;
    public int superHitInterval = 7;
    public int superHitDamage = 45;
    public int superFinisherDamage = 140;
    public int superChipDamage = 60;
    public int superBlockstun = 20;
    public int superRecoveryBlocked = 40;
    public int superRecoveryWhiff = 45;
    public int superRecoveryAfterHit = 24;
    public float superMinScaling = 0.7f;
}
