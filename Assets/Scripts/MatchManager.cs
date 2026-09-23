using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs the match: fixed 60 Hz simulation, hit resolution, rounds, and events for the HUD.
public class MatchManager : MonoBehaviour
{
    public const float Step = 1f / 60f;

    public enum Phase { Intro, Fight, RoundOver, MatchOver }

    public class FloatingText
    {
        public Fighter owner;
        public string text;
        public Color color;
        public Vector3 world;
        public float age;
        public float life;
    }

    struct PendingHit
    {
        public Fighter atk, def;
        public AttackData data;
        public bool super;
        public Vector3 point;
    }

    [Header("References")]
    public FighterConfig config;
    public Fighter player1;
    public Fighter player2;
    public CameraRig cameraRig;

    [Header("Match rules")]
    public int roundsToWin = 2;
    public int roundSeconds = 60;
    public float stageHalfWidth = 11f;
    public float maxSeparation = 9.5f;
    public float startDistance = 3.6f;

    [Header("Controls (one shared keyboard)")]
    public FighterControls p1Controls = new FighterControls(Key.A, Key.D, Key.W, Key.S, Key.F, Key.G, Key.H, Key.R);
    public FighterControls p2Controls = new FighterControls(Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow,
        Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad5);

    [Header("Debug")]
    public bool showHitboxes;
    public bool trainingMode;

    [System.NonSerialized] public Phase phase;
    [System.NonSerialized] public bool paused;
    [System.NonSerialized] public int round;
    [System.NonSerialized] public int[] wins = new int[2];
    [System.NonSerialized] public int timerFrames;
    [System.NonSerialized] public int hitstop;
    [System.NonSerialized] public int freezeFrames;
    [System.NonSerialized] public Fighter superFlashOwner;
    [System.NonSerialized] public string bannerText;
    [System.NonSerialized] public int bannerFrames;
    [System.NonSerialized] public string matchResultText;
    [System.NonSerialized] public int[] comboShow = new int[2];
    [System.NonSerialized] public float[] comboShowTime = new float[2];
    public readonly List<FloatingText> popups = new List<FloatingText>();

    readonly List<PendingHit> pending = new List<PendingHit>();
    readonly float[] superScale = { 1f, 1f };
    int phaseFrames;
    int roundWinner;
    float accumulator;
    float slowmoTime;
    bool ready;

    public int FighterIndex(Fighter f) => f == player1 ? 0 : 1;

    void Start()
    {
        if (config == null) config = ScriptableObject.CreateInstance<FighterConfig>();
        if (cameraRig == null && Camera.main != null) cameraRig = Camera.main.GetComponent<CameraRig>();
        if (player1 == null || player2 == null)
        {
            Debug.LogError("MatchManager: assign both fighters.");
            enabled = false;
            return;
        }
        player1.playerIndex = 0;
        player2.playerIndex = 1;
        player1.Init(this, config, player2);
        player2.Init(this, config, player1);
        if (cameraRig != null) cameraRig.match = this;
        ready = true;
        StartMatch();
    }

    void Update()
    {
        if (!ready) return;
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            p1Controls.Poll(kb);
            p2Controls.Poll(kb);
            if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) paused = !paused;
            if (kb.f1Key.wasPressedThisFrame) showHitboxes = !showHitboxes;
            if (kb.f2Key.wasPressedThisFrame) trainingMode = !trainingMode;
            if (kb.f3Key.wasPressedThisFrame)
            {
                player1.flux = config.maxFlux;
                player2.flux = config.maxFlux;
            }
            if (kb.backspaceKey.wasPressedThisFrame) StartMatch();
            if (phase == Phase.MatchOver && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                StartMatch();
        }

        for (int i = popups.Count - 1; i >= 0; i--)
        {
            popups[i].age += Time.deltaTime;
            if (popups[i].age >= popups[i].life) popups.RemoveAt(i);
        }
        if (paused) return;

        for (int i = 0; i < 2; i++) comboShowTime[i] -= Time.deltaTime;

        float speed = 1f;
        if (slowmoTime > 0f)
        {
            slowmoTime -= Time.unscaledDeltaTime;
            speed = 0.3f;
        }
        accumulator += Time.deltaTime * speed;
        int steps = 0;
        while (accumulator >= Step && steps < 4)
        {
            accumulator -= Step;
            SimTick();
            steps++;
        }
        if (accumulator > Step * 4f) accumulator = 0f;
    }

    // Advances the whole game by exactly one 60 Hz frame. Public so it can be driven by scripts/tests.
    public void SimTick()
    {
        Keyboard kb = Keyboard.current;
        bool frozen = freezeFrames > 0 || hitstop > 0;
        p1Controls.Step(kb, config.inputBufferFrames, !frozen);
        p2Controls.Step(kb, config.inputBufferFrames, !frozen);

        if (freezeFrames > 0)
        {
            if (--freezeFrames == 0) superFlashOwner = null;
            return;
        }
        if (hitstop > 0)
        {
            hitstop--;
            return;
        }

        bool inputOn = phase == Phase.Fight;
        player1.inputEnabled = inputOn;
        player2.inputEnabled = inputOn;
        player1.cannotDie = trainingMode;
        player2.cannotDie = trainingMode;

        float prev1 = player1.pos.x, prev2 = player2.pos.x;
        player1.Tick();
        player2.Tick();
        ResolveBodies(prev1, prev2);
        DetectHits();

        if (trainingMode)
        {
            foreach (Fighter f in new[] { player1, player2 })
                if (f.neutralFrames >= 60 && f.health < config.maxHealth) f.health = config.maxHealth;
        }
        TickPhase();
    }

    // ---------------------------------------------------------------- rounds

    public void StartMatch()
    {
        wins[0] = wins[1] = 0;
        round = 1;
        player1.ResetForMatch();
        player2.ResetForMatch();
        popups.Clear();
        paused = false;
        slowmoTime = 0f;
        StartRound();
    }

    void StartRound()
    {
        bool resetFlux = !config.fluxCarriesOverBetweenRounds;
        player1.ResetForRound(-startDistance * 0.5f, resetFlux);
        player2.ResetForRound(startDistance * 0.5f, resetFlux);
        player1.FaceOpponent();
        player2.FaceOpponent();
        p1Controls.Clear();
        p2Controls.Clear();
        hitstop = 0;
        freezeFrames = 0;
        superFlashOwner = null;
        timerFrames = roundSeconds * 60;
        comboShowTime[0] = comboShowTime[1] = 0f;
        SetPhase(Phase.Intro);
        bool finalRound = wins[0] == roundsToWin - 1 && wins[1] == roundsToWin - 1;
        ShowBanner(finalRound ? "FINAL ROUND" : "ROUND " + round, 60);
    }

    void SetPhase(Phase p)
    {
        phase = p;
        phaseFrames = 0;
    }

    void ShowBanner(string text, int frames)
    {
        bannerText = text;
        bannerFrames = frames;
    }

    void TickPhase()
    {
        phaseFrames++;
        if (bannerFrames > 0) bannerFrames--;

        switch (phase)
        {
            case Phase.Intro:
                if (phaseFrames >= 60)
                {
                    SetPhase(Phase.Fight);
                    ShowBanner("FIGHT!", 40);
                }
                break;

            case Phase.Fight:
                if (!trainingMode && timerFrames > 0) timerFrames--;
                bool ko1 = player1.health <= 0, ko2 = player2.health <= 0;
                if (ko1 || ko2) EndRound(ko1 && ko2 ? -1 : ko1 ? 1 : 0, "K.O.");
                else if (!trainingMode && timerFrames <= 0)
                    EndRound(player1.health > player2.health ? 0 : player2.health > player1.health ? 1 : -1, "TIME");
                break;

            case Phase.RoundOver:
                if (phaseFrames == 80)
                    ShowBanner(roundWinner < 0 ? "DRAW" : (roundWinner == 0 ? "P1" : "P2") + " WINS THE ROUND", 90);
                if (phaseFrames >= 180)
                {
                    if (wins[0] >= roundsToWin || wins[1] >= roundsToWin)
                    {
                        SetPhase(Phase.MatchOver);
                        bannerFrames = 0;
                        matchResultText = wins[0] == wins[1] ? "DRAW GAME" : wins[0] > wins[1] ? "PLAYER 1 WINS" : "PLAYER 2 WINS";
                    }
                    else
                    {
                        round++;
                        StartRound();
                    }
                }
                break;
        }
    }

    void EndRound(int winner, string label)
    {
        if (winner < 0)
        {
            wins[0]++;
            wins[1]++;
        }
        else wins[winner]++;
        roundWinner = winner;
        SetPhase(Phase.RoundOver);
        ShowBanner(label, 80);
        if (label == "K.O.")
        {
            slowmoTime = 1.0f;
            if (cameraRig) cameraRig.Shake(0.2f, 0.4f);
        }
    }

    // ---------------------------------------------------------------- bodies & walls

    void ResolveBodies(float prev1, float prev2)
    {
        Fighter a = player1, b = player2;
        float limit = stageHalfWidth - config.bodyHalfWidth;

        // Camera leash: whoever moved away gets held back.
        float dist = Mathf.Abs(a.pos.x - b.pos.x);
        if (dist > maxSeparation)
        {
            float excess = dist - maxSeparation;
            float sign = Mathf.Sign(b.pos.x - a.pos.x);
            float outA = Mathf.Max(0f, -(a.pos.x - prev1) * sign);
            float outB = Mathf.Max(0f, (b.pos.x - prev2) * sign);
            float total = outA + outB;
            float shareA = total > 0.0001f ? outA / total : 0.5f;
            a.pos.x += sign * excess * shareA;
            b.pos.x -= sign * excess * (1f - shareA);
        }
        a.pos.x = Mathf.Clamp(a.pos.x, -limit, limit);
        b.pos.x = Mathf.Clamp(b.pos.x, -limit, limit);

        // Pushboxes. Only when they overlap vertically, so you can jump over (cross-ups).
        bool vertical = a.pos.y < b.pos.y + b.PushboxHeight && b.pos.y < a.pos.y + a.PushboxHeight;
        if (!vertical) return;
        float minDist = config.bodyHalfWidth * 2f;
        float dx = b.pos.x - a.pos.x;
        if (Mathf.Abs(dx) >= minDist) return;

        float side = Mathf.Abs(dx) > 0.0001f ? Mathf.Sign(dx)
            : Mathf.Abs(prev2 - prev1) > 0.0001f ? Mathf.Sign(prev2 - prev1) : a.facing;
        float half = (minDist - Mathf.Abs(dx)) * 0.5f;
        a.pos.x = Mathf.Clamp(a.pos.x - side * half, -limit, limit);
        b.pos.x = Mathf.Clamp(b.pos.x + side * half, -limit, limit);
        if (Mathf.Abs(b.pos.x - a.pos.x) < minDist - 0.0001f)
        {
            // One of them is against a wall; push the other one out.
            bool aAtWall = Mathf.Abs(a.pos.x) >= limit - 0.0001f;
            if (aAtWall) b.pos.x = Mathf.Clamp(a.pos.x + side * minDist, -limit, limit);
            else a.pos.x = Mathf.Clamp(b.pos.x - side * minDist, -limit, limit);
        }
    }

    void Push(Fighter atk, Fighter def, int dir, float amount)
    {
        float limit = stageHalfWidth - config.bodyHalfWidth;
        bool defAtWall = dir > 0 ? def.pos.x >= limit - 0.3f : def.pos.x <= -limit + 0.3f;
        if (defAtWall) atk.AddSlide(-dir * amount);
        else def.AddSlide(dir * amount);
    }

    // ---------------------------------------------------------------- hits

    void DetectHits()
    {
        pending.Clear();
        FindHit(player1, player2);
        FindHit(player2, player1);
        foreach (PendingHit h in pending)
        {
            if (h.super) ApplySuperDash(h.atk, h.def, h.point);
            else ApplyHit(h.atk, h.def, h.data, h.point);
        }
    }

    void FindHit(Fighter atk, Fighter def)
    {
        if (!def.CanBeHit) return;
        Rect hurt = def.Hurtbox;
        if (atk.TryGetHitbox(out Rect box, out AttackData data) && box.Overlaps(hurt))
            pending.Add(new PendingHit { atk = atk, def = def, data = data, point = OverlapCenter(box, hurt) });
        else if (atk.TryGetSuperHitbox(out box) && box.Overlaps(hurt))
            pending.Add(new PendingHit { atk = atk, def = def, super = true, point = OverlapCenter(box, hurt) });
    }

    static Vector3 OverlapCenter(Rect a, Rect b)
    {
        float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
        float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax);
        return new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, -0.3f);
    }

    static int HitDir(Fighter atk, Fighter def)
    {
        float dx = def.pos.x - atk.pos.x;
        return Mathf.Abs(dx) > 0.001f ? (dx > 0f ? 1 : -1) : atk.facing;
    }

    public float ComboScaling(int hitsBefore)
    {
        int n = hitsBefore + 1;
        if (n < config.scalingStartsAtHit) return 1f;
        return Mathf.Max(config.minScaling, 1f - config.scalingPerHit * (n - config.scalingStartsAtHit + 1));
    }

    void ApplyHit(Fighter atk, Fighter def, AttackData d, Vector3 point)
    {
        int dir = HitDir(atk, def);
        bool blocked = def.CanBlock(d.guard, atk.pos.x);
        atk.OnAttackConnected(d, blocked);
        if (blocked)
        {
            def.ReceiveBlock(d.blockstun, 0, dir);
            Push(atk, def, dir, d.pushbackBlock);
            hitstop = Mathf.Max(hitstop, d.hitstop - 2);
            HitSpark.Spawn(point, new Color(0.7f, 0.9f, 1f), 0.35f);
            return;
        }
        int dmg = Mathf.Max(1, Mathf.RoundToInt(d.damage * ComboScaling(def.comboHits)));
        def.ReceiveHit(dmg, d.hitstun, d.knockdown, dir);
        atk.stats.damageDealt += dmg;
        Push(atk, def, dir, d.pushbackHit);
        hitstop = Mathf.Max(hitstop, d.hitstop);
        HitSpark.Spawn(point, AttackData.TypeColor(d.type), d.type == AttackType.Heavy ? 0.8f : 0.55f);
        if (cameraRig) cameraRig.Shake(d.type == AttackType.Heavy ? 0.12f : 0.04f, 0.12f);
        RegisterCombo(atk, def);
    }

    void ApplySuperDash(Fighter atk, Fighter def, Vector3 point)
    {
        int dir = HitDir(atk, def);
        bool blocked = def.CanBlock(GuardType.Mid, atk.pos.x);
        atk.OnSuperDashConnected(blocked);
        if (blocked)
        {
            def.ReceiveBlock(config.superBlockstun, config.superChipDamage, dir);
            Push(atk, def, dir, 1.0f);
            hitstop = Mathf.Max(hitstop, 12);
            HitSpark.Spawn(point, new Color(0.7f, 0.9f, 1f), 0.7f);
            ShowPopup(atk, "RUSH BLOCKED", Color.white);
            if (cameraRig) cameraRig.Shake(0.08f, 0.15f);
            return;
        }
        ShowPopup(atk, "RUSH HIT!", AttackData.TypeColor(AttackType.Super));
        // Only the combo leading into the Rush scales it, not the Rush's own hits.
        superScale[FighterIndex(atk)] = Mathf.Max(config.superMinScaling, ComboScaling(def.comboHits));
        SuperHit(atk, def, false, point);
    }

    public void ApplySuperSequenceHit(Fighter atk, Fighter def, bool final)
    {
        if (def.state == FighterState.KO) return;
        Vector3 point = new Vector3(def.pos.x - HitDir(atk, def) * config.bodyHalfWidth, def.pos.y + 1.2f, -0.3f);
        SuperHit(atk, def, final, point);
    }

    void SuperHit(Fighter atk, Fighter def, bool final, Vector3 point)
    {
        int dir = HitDir(atk, def);
        float scale = superScale[FighterIndex(atk)];
        int dmg = Mathf.Max(1, Mathf.RoundToInt((final ? config.superFinisherDamage : config.superHitDamage) * scale));
        def.ReceiveHit(dmg, config.superHitInterval + 8, final, dir, true);
        atk.stats.damageDealt += dmg;
        atk.stats.superDamage += dmg;
        Push(atk, def, dir, final ? 0.8f : 0.12f);
        hitstop = Mathf.Max(hitstop, final ? 16 : 4);
        HitSpark.Spawn(point, AttackData.TypeColor(AttackType.Super), final ? 1.1f : 0.6f);
        if (cameraRig) cameraRig.Shake(final ? 0.25f : 0.06f, final ? 0.3f : 0.08f);
        RegisterCombo(atk, def);
        if (def.state == FighterState.KO) atk.EndSuperSequence();
    }

    void RegisterCombo(Fighter atk, Fighter def)
    {
        if (def.comboHits < 2) return;
        int i = FighterIndex(atk);
        comboShow[i] = def.comboHits;
        comboShowTime[i] = 1.5f;
    }

    // ---------------------------------------------------------------- feedback events

    public void ShowPopup(Fighter f, string text, Color color)
    {
        int stack = 0;
        foreach (FloatingText p in popups)
            if (p.owner == f && p.age < 0.6f) stack++;
        popups.Add(new FloatingText
        {
            owner = f,
            text = text,
            color = color,
            world = new Vector3(f.pos.x, f.pos.y + 2.5f + stack * 0.4f, 0f),
            life = 1.2f
        });
    }

    public void OnFluxGained(Fighter f, float gain, bool blocked, bool becameFull)
    {
        ShowPopup(f, "+" + Mathf.RoundToInt(gain) + " FLUX" + (blocked ? " (blocked)" : ""), new Color(0.78f, 0.5f, 1f));
        if (becameFull) ShowPopup(f, "FLUX FULL!", AttackData.TypeColor(AttackType.Super));
    }

    public void OnRepeatAttack(Fighter f, AttackType t)
    {
        ShowPopup(f, "REPEAT " + t.ToString().ToUpper() + " - no flux", new Color(0.6f, 0.6f, 0.65f));
    }

    public void OnSuperDenied(Fighter f)
    {
        ShowPopup(f, "flux not full", new Color(0.6f, 0.6f, 0.65f));
    }

    public void OnSuperActivated(Fighter f)
    {
        freezeFrames = config.superFreezeFrames;
        superFlashOwner = f;
        ShowPopup(f, "RUSH!", AttackData.TypeColor(AttackType.Super));
        if (cameraRig) cameraRig.Shake(0.1f, 0.2f);
    }
}
