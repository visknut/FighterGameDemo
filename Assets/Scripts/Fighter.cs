using UnityEngine;

public enum FighterState { Idle, Walk, Crouch, JumpSquat, Air, AirAttack, Landing, Attack, Hitstun, Blockstun, AirHit, Knockdown, Super, KO }

public class FighterStats
{
    public int connects, alternations, supersUsed, supersLanded, supersBlocked, superDamage, damageDealt;
    public float fluxGained;
}

// One fighter. The simulation runs in fixed 60 Hz ticks driven by MatchManager; visuals update every frame.
public class Fighter : MonoBehaviour
{
    const float Dt = MatchManager.Step;

    enum SuperPhase { Startup, Dash, Hits, Recovery }

    public int playerIndex;
    public Color baseColor = new Color(0.25f, 0.5f, 1f);

    [HideInInspector] public MatchManager match;
    [HideInInspector] public FighterConfig cfg;
    [HideInInspector] public Fighter opponent;
    [HideInInspector] public bool inputEnabled;
    [HideInInspector] public bool cannotDie;

    [Header("Runtime (read only)")]
    public FighterState state;
    public Vector2 pos;
    public Vector2 vel;
    public int facing = 1;
    public int health;
    public float flux;
    public AttackType lastConnectedType;
    public bool crouching;
    public int comboHits;
    public int invulnFrames;
    public int neutralFrames;

    [System.NonSerialized] public FighterStats stats = new FighterStats();

    // Looked up every time: Unity may replace the manager's serialized controls objects (e.g. inspector edits).
    public FighterControls input => match == null ? null : playerIndex == 0 ? match.p1Controls : match.p2Controls;

    AttackData attack;
    int attackFrame;
    bool attackHasHit;
    bool attackConnected;
    bool usedLight, usedHeavy, usedLow;
    bool airAttackUsed;
    int stateFrame;
    int stunFrames;
    float slide;

    SuperPhase superPhase;
    int superHitIndex;
    int superRecovery;
    bool superHasHit;

    Transform visual, body, head, eye, limb, shield;
    Renderer bodyR, headR, eyeR, limbR, shieldR;
    float hitFlash;

    static readonly Color DarkGrey = new Color(0.12f, 0.12f, 0.14f);

    public bool IsAirborne => state == FighterState.Air || state == FighterState.AirAttack || state == FighterState.AirHit
                              || (state == FighterState.KO && pos.y > 0.001f);
    public bool IsActionable => state == FighterState.Idle || state == FighterState.Walk || state == FighterState.Crouch;
    public bool IsFluxFull => cfg != null && flux >= cfg.maxFlux - 0.001f;
    public bool CanBeHit => invulnFrames <= 0 && state != FighterState.Knockdown && state != FighterState.KO;
    public bool IsReeling => state == FighterState.Hitstun || state == FighterState.Blockstun || state == FighterState.AirHit || state == FighterState.KO;
    public AttackData CurrentAttack => attack;
    public int AttackFrame => attackFrame;
    public float PushboxHeight => IsAirborne ? 1.2f : crouching ? cfg.crouchHeight : 1.8f;

    public Rect Hurtbox
    {
        get
        {
            float h = state == FighterState.Knockdown || state == FighterState.KO ? 0.5f
                : crouching ? cfg.crouchHeight : cfg.standHeight;
            return new Rect(pos.x - cfg.bodyHalfWidth, pos.y, cfg.bodyHalfWidth * 2f, h);
        }
    }

    int InputX()
    {
        if (!inputEnabled || input == null) return 0;
        return (input.holdRight ? 1 : 0) - (input.holdLeft ? 1 : 0);
    }

    bool HoldDown => inputEnabled && input != null && input.holdDown;
    bool HoldUp => inputEnabled && input != null && input.holdUp;

    void Awake()
    {
        EnsureRig();
    }

    public void Init(MatchManager m, FighterConfig config, Fighter opp)
    {
        match = m;
        cfg = config;
        opponent = opp;
        health = cfg.maxHealth;
    }

    public void ResetForMatch()
    {
        flux = 0f;
        stats = new FighterStats();
    }

    public void ResetForRound(float x, bool resetFlux)
    {
        inputEnabled = false;
        pos = new Vector2(x, 0f);
        vel = Vector2.zero;
        slide = 0f;
        health = cfg.maxHealth;
        if (resetFlux) flux = 0f;
        lastConnectedType = AttackType.None;
        invulnFrames = 0;
        stunFrames = 0;
        hitFlash = 0f;
        ToNeutral();
    }

    public void FaceOpponent()
    {
        float dx = opponent.pos.x - pos.x;
        if (Mathf.Abs(dx) > 0.01f) facing = dx > 0f ? 1 : -1;
    }

    void SetState(FighterState s)
    {
        state = s;
        stateFrame = 0;
    }

    void ToNeutral()
    {
        SetState(FighterState.Idle);
        attack = null;
        usedLight = usedHeavy = usedLow = false;
        comboHits = 0;
        crouching = HoldDown;
        if (crouching) state = FighterState.Crouch;
    }

    // ---------------------------------------------------------------- simulation

    public void Tick()
    {
        stateFrame++;
        if (invulnFrames > 0) invulnFrames--;

        switch (state)
        {
            case FighterState.Idle:
            case FighterState.Walk:
            case FighterState.Crouch:
                TickNeutral();
                break;
            case FighterState.JumpSquat:
                if (stateFrame >= cfg.jumpSquatFrames) StartJump();
                break;
            case FighterState.Air:
                TickAir();
                break;
            case FighterState.AirAttack:
                TickAirAttack();
                break;
            case FighterState.Landing:
                if (stateFrame >= cfg.landingRecoveryFrames) ToNeutral();
                break;
            case FighterState.Attack:
                TickAttack();
                break;
            case FighterState.Hitstun:
                if (--stunFrames <= 0) ToNeutral();
                break;
            case FighterState.Blockstun:
                crouching = HoldDown;
                if (--stunFrames <= 0) ToNeutral();
                break;
            case FighterState.AirHit:
                if (AirPhysics())
                {
                    SetState(FighterState.Knockdown);
                    invulnFrames = cfg.knockdownFrames + 2;
                }
                break;
            case FighterState.Knockdown:
                if (stateFrame >= cfg.knockdownFrames)
                {
                    ToNeutral();
                    FaceOpponent();
                }
                break;
            case FighterState.Super:
                TickSuper();
                break;
            case FighterState.KO:
                if (pos.y > 0f || vel.y > 0f) AirPhysics();
                break;
        }

        ApplySlide();
        neutralFrames = IsActionable ? neutralFrames + 1 : 0;
    }

    void TickNeutral()
    {
        FaceOpponent();
        if (inputEnabled)
        {
            if (TrySuper()) return;
            AttackType a = input.ConsumeAttack(true, true, true);
            if (a != AttackType.None)
            {
                StartGroundAttack(a);
                return;
            }
            if (HoldUp)
            {
                crouching = false;
                SetState(FighterState.JumpSquat);
                return;
            }
        }

        int x = InputX();
        if (HoldDown)
        {
            crouching = true;
            state = FighterState.Crouch;
        }
        else if (x != 0)
        {
            crouching = false;
            state = FighterState.Walk;
            pos.x += x * (x == facing ? cfg.walkForwardSpeed : cfg.walkBackSpeed) * Dt;
        }
        else
        {
            crouching = false;
            state = FighterState.Idle;
        }
    }

    bool TrySuper()
    {
        if (input.bufSpecial <= 0) return false;
        input.bufSpecial = 0;
        if (!IsFluxFull)
        {
            match.OnSuperDenied(this);
            return false;
        }
        StartSuper();
        return true;
    }

    void StartGroundAttack(AttackType t)
    {
        attack = t == AttackType.Light ? cfg.light : t == AttackType.Heavy ? cfg.heavy : cfg.low;
        attackFrame = 1;
        attackHasHit = false;
        attackConnected = false;
        if (t == AttackType.Light) usedLight = true;
        else if (t == AttackType.Heavy) usedHeavy = true;
        else usedLow = true;
        crouching = t == AttackType.Low;
        SetState(FighterState.Attack);
    }

    void TickAttack()
    {
        attackFrame++;
        // Chain cancel: once an attack connects (hit or block) it can cancel into an attack type
        // not yet used in this chain, or into the Rush super.
        if (attackConnected && inputEnabled)
        {
            if (input.bufSpecial > 0 && IsFluxFull)
            {
                input.bufSpecial = 0;
                StartSuper();
                return;
            }
            AttackType next = input.ConsumeAttack(!usedLight, !usedHeavy, !usedLow);
            if (next != AttackType.None)
            {
                StartGroundAttack(next);
                return;
            }
        }
        if (attackFrame >= attack.TotalFrames) ToNeutral();
    }

    void StartJump()
    {
        int x = InputX();
        vel = new Vector2(x * cfg.jumpHorizontalSpeed, cfg.jumpVelocity);
        airAttackUsed = false;
        SetState(FighterState.Air);
    }

    // Returns true on the tick the fighter lands.
    bool AirPhysics()
    {
        pos += vel * Dt;
        vel.y -= cfg.gravity * Dt;
        if (pos.y <= 0f && vel.y < 0f)
        {
            pos.y = 0f;
            vel = Vector2.zero;
            return true;
        }
        return false;
    }

    void TickAir()
    {
        if (AirPhysics())
        {
            SetState(FighterState.Landing);
            return;
        }
        if (!inputEnabled || airAttackUsed) return;
        AttackType a = input.ConsumeAttack(true, true, true);
        if (a == AttackType.None) return;
        attack = a == AttackType.Light ? cfg.airLight : a == AttackType.Heavy ? cfg.airHeavy : cfg.airLow;
        attackFrame = 1;
        attackHasHit = false;
        attackConnected = false;
        airAttackUsed = true;
        SetState(FighterState.AirAttack);
    }

    void TickAirAttack()
    {
        attackFrame++;
        if (AirPhysics())
        {
            attack = null;
            SetState(FighterState.Landing);
            return;
        }
        if (attackFrame >= attack.TotalFrames)
        {
            attack = null;
            state = FighterState.Air;
        }
    }

    void StartSuper()
    {
        flux = 0f;
        lastConnectedType = AttackType.None;
        stats.supersUsed++;
        attack = null;
        crouching = false;
        superPhase = SuperPhase.Startup;
        superHasHit = false;
        superHitIndex = 0;
        SetState(FighterState.Super);
        FaceOpponent();
        invulnFrames = cfg.superInvulnFrames;
        match.OnSuperActivated(this);
    }

    void TickSuper()
    {
        switch (superPhase)
        {
            case SuperPhase.Startup:
                if (stateFrame >= cfg.superStartupFrames)
                {
                    superPhase = SuperPhase.Dash;
                    stateFrame = 0;
                }
                break;
            case SuperPhase.Dash:
                pos.x += facing * cfg.superDashSpeed * Dt;
                if (stateFrame >= cfg.superDashFrames)
                {
                    superPhase = SuperPhase.Recovery;
                    superRecovery = cfg.superRecoveryWhiff;
                    stateFrame = 0;
                }
                break;
            case SuperPhase.Hits:
                float targetX = opponent.pos.x - facing * (cfg.bodyHalfWidth * 2f + 0.05f);
                pos.x = Mathf.MoveTowards(pos.x, targetX, 0.2f);
                if (stateFrame >= cfg.superHitInterval)
                {
                    stateFrame = 0;
                    superHitIndex++;
                    bool final = superHitIndex >= cfg.superHits - 1;
                    if (final) EndSuperSequence();
                    match.ApplySuperSequenceHit(this, opponent, final);
                }
                break;
            case SuperPhase.Recovery:
                if (stateFrame >= superRecovery) ToNeutral();
                break;
        }
    }

    public void OnSuperDashConnected(bool blocked)
    {
        superHasHit = true;
        stateFrame = 0;
        if (blocked)
        {
            superPhase = SuperPhase.Recovery;
            superRecovery = cfg.superRecoveryBlocked;
            stats.supersBlocked++;
        }
        else
        {
            superPhase = SuperPhase.Hits;
            superHitIndex = 0;
            stats.supersLanded++;
        }
    }

    public void EndSuperSequence()
    {
        superPhase = SuperPhase.Recovery;
        superRecovery = cfg.superRecoveryAfterHit;
        stateFrame = 0;
    }

    void ApplySlide()
    {
        if (Mathf.Abs(slide) < 0.001f)
        {
            slide = 0f;
            return;
        }
        float step = slide * 0.25f;
        pos.x += step;
        slide -= step;
    }

    public void AddSlide(float amount) => slide += amount;

    // ---------------------------------------------------------------- hit interface

    public bool TryGetHitbox(out Rect box, out AttackData data)
    {
        box = default;
        data = null;
        if ((state != FighterState.Attack && state != FighterState.AirAttack) || attack == null) return false;
        if (attackHasHit || !attack.IsActiveFrame(attackFrame)) return false;
        box = MakeBox(attack.hitboxOffset, attack.hitboxSize);
        data = attack;
        return true;
    }

    public bool TryGetSuperHitbox(out Rect box)
    {
        box = default;
        if (state != FighterState.Super || superPhase != SuperPhase.Dash || superHasHit) return false;
        box = MakeBox(cfg.superHitboxOffset, cfg.superHitboxSize);
        return true;
    }

    Rect MakeBox(Vector2 offset, Vector2 size)
    {
        float cx = pos.x + facing * offset.x;
        float cy = pos.y + offset.y;
        return new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f, size.x, size.y);
    }

    public bool CanBlock(GuardType guard, float attackerX)
    {
        if (!inputEnabled) return false;
        bool inBlockstun = state == FighterState.Blockstun;
        if (!IsActionable && !inBlockstun) return false;
        // Holding away from the attacker blocks. Once in blockstun you keep blocking (true blockstrings),
        // but you still have to match high/low.
        if (!inBlockstun)
        {
            int away = attackerX > pos.x + 0.001f ? -1 : attackerX < pos.x - 0.001f ? 1 : -facing;
            if (InputX() != away) return false;
        }
        bool low = HoldDown;
        switch (guard)
        {
            case GuardType.High: return !low;
            case GuardType.Low: return low;
            default: return true;
        }
    }

    // Called on the attacker when its normal connects. This is where flux is earned.
    public void OnAttackConnected(AttackData data, bool blocked)
    {
        attackHasHit = true;
        attackConnected = true;
        stats.connects++;
        if (data.type != lastConnectedType)
        {
            float gain = data.fluxGain * (blocked ? cfg.blockedFluxMultiplier : 1f);
            float before = flux;
            flux = Mathf.Min(cfg.maxFlux, flux + gain);
            stats.alternations++;
            stats.fluxGained += flux - before;
            match.OnFluxGained(this, gain, blocked, before < cfg.maxFlux && IsFluxFull);
        }
        else
        {
            match.OnRepeatAttack(this, data.type);
        }
        lastConnectedType = data.type;
    }

    public void ReceiveBlock(int blockstun, int chip, int dirX)
    {
        if (chip > 0)
        {
            health -= chip;
            if (health <= 0) health = cannotDie ? 1 : 0;
            if (health <= 0)
            {
                GoKO(dirX);
                return;
            }
        }
        attack = null;
        crouching = HoldDown;
        SetState(FighterState.Blockstun);
        stunFrames = blockstun;
    }

    public void ReceiveHit(int damage, int hitstun, bool launch, int dirX, bool forceGrounded = false)
    {
        bool wasAirborne = IsAirborne;
        health -= damage;
        if (health <= 0) health = cannotDie ? 1 : 0;
        comboHits++;
        hitFlash = 1f;
        attack = null;
        usedLight = usedHeavy = usedLow = false;
        if (forceGrounded)
        {
            pos.y = 0f;
            vel = Vector2.zero;
            wasAirborne = false;
        }

        if (health <= 0)
        {
            GoKO(dirX);
            return;
        }
        if (launch || wasAirborne)
        {
            crouching = false;
            SetState(FighterState.AirHit);
            vel = new Vector2(dirX * cfg.launchKnockback, cfg.launchVelocity);
            return;
        }
        SetState(FighterState.Hitstun);
        stunFrames = hitstun;
    }

    void GoKO(int dirX)
    {
        crouching = false;
        attack = null;
        SetState(FighterState.KO);
        vel = new Vector2(dirX * 3f, 7f);
    }

    // ---------------------------------------------------------------- visuals

    void EnsureRig()
    {
        visual = transform.Find("Visual");
        if (visual == null)
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
        }
        body = Part("Body", PrimitiveType.Capsule, out bodyR);
        head = Part("Head", PrimitiveType.Sphere, out headR);
        eye = Part("Eye", PrimitiveType.Cube, out eyeR);
        limb = Part("Limb", PrimitiveType.Cube, out limbR);
        shield = Part("Shield", PrimitiveType.Cube, out shieldR);
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) Destroy(c);
        limb.gameObject.SetActive(false);
        shield.gameObject.SetActive(false);
    }

    Transform Part(string partName, PrimitiveType type, out Renderer r)
    {
        Transform t = visual.Find(partName);
        if (t == null)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            t = go.transform;
            t.SetParent(visual, false);
        }
        r = t.GetComponent<Renderer>();
        return t;
    }

    void LateUpdate()
    {
        if (cfg == null || visual == null) return;
        transform.position = new Vector3(pos.x, pos.y, 0f);

        bool lying = state == FighterState.Knockdown || (state == FighterState.KO && pos.y <= 0.001f);
        float tilt = lying ? 85f
            : state == FighterState.AirHit || state == FighterState.KO ? 40f
            : state == FighterState.Hitstun ? 12f : 0f;
        visual.localRotation = Quaternion.Euler(0f, facing == 1 ? 0f : 180f, 0f) * Quaternion.Euler(0f, 0f, tilt);

        Vector3 shake = Vector3.zero;
        if (match != null && match.hitstop > 0 && IsReeling) shake.x = Random.Range(-0.06f, 0.06f);
        visual.localPosition = new Vector3(0f, lying ? 0.35f : 0f, 0f) + shake;

        float h = crouching && !lying ? 0.55f : 1f;
        body.localScale = new Vector3(0.8f, 0.8f * h, 0.6f);
        body.localPosition = new Vector3(0f, 0.8f * h, 0f);
        head.localScale = Vector3.one * 0.45f;
        head.localPosition = new Vector3(0.05f, 1.6f * h + 0.22f, 0f);
        eye.localScale = new Vector3(0.14f, 0.08f, 0.32f);
        eye.localPosition = head.localPosition + new Vector3(0.2f, 0.05f, 0f);

        hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 5f);
        Color superColor = AttackData.TypeColor(AttackType.Super);
        Color c = baseColor;
        if (state == FighterState.Super) c = superColor;
        else if (IsFluxFull) c = Color.Lerp(baseColor, superColor, 0.35f + 0.35f * Mathf.Sin(Time.time * 12f));
        if (state == FighterState.Knockdown) c = Color.Lerp(c, Color.gray, 0.5f);
        if (state == FighterState.KO) c = Color.Lerp(c, Color.black, 0.5f);
        c = Color.Lerp(c, Color.white, hitFlash);
        bodyR.material.color = c;
        headR.material.color = Color.Lerp(c, Color.white, 0.25f);
        eyeR.material.color = DarkGrey;

        UpdateLimb();

        bool blocking = state == FighterState.Blockstun;
        shield.gameObject.SetActive(blocking);
        if (blocking)
        {
            shield.localPosition = new Vector3(0.6f, crouching ? 0.55f : 1.0f, 0f);
            shield.localScale = new Vector3(0.08f, crouching ? 1.1f : 1.9f, 0.9f);
            shieldR.material.color = new Color(0.6f, 0.9f, 1f);
        }
    }

    void UpdateLimb()
    {
        bool show = false;
        if ((state == FighterState.Attack || state == FighterState.AirAttack) && attack != null)
        {
            show = true;
            AttackData a = attack;
            float ext, bright;
            if (attackFrame <= a.startup)
            {
                ext = Mathf.Lerp(0.2f, 0.45f, attackFrame / (float)Mathf.Max(1, a.startup));
                bright = 0.45f;
            }
            else if (attackFrame <= a.startup + a.active)
            {
                ext = 1f;
                bright = 1f;
            }
            else
            {
                float r = (attackFrame - a.startup - a.active) / (float)Mathf.Max(1, a.recovery);
                ext = Mathf.Lerp(0.85f, 0.2f, r);
                bright = 0.55f;
            }
            PlaceLimb(a.hitboxOffset, a.hitboxSize, ext, Color.Lerp(DarkGrey, AttackData.TypeColor(a.type), bright));
        }
        else if (state == FighterState.Super && (superPhase == SuperPhase.Dash || superPhase == SuperPhase.Hits))
        {
            show = true;
            float ext = superPhase == SuperPhase.Dash ? 1f : (stateFrame % cfg.superHitInterval < 3 ? 1f : 0.4f);
            PlaceLimb(new Vector2(0.75f, 1.2f), new Vector2(0.9f, 0.4f), ext, AttackData.TypeColor(AttackType.Super));
        }
        limb.gameObject.SetActive(show);
    }

    void PlaceLimb(Vector2 offset, Vector2 size, float ext, Color c)
    {
        const float near = 0.1f;
        float far = offset.x + size.x * 0.5f;
        float len = Mathf.Max(0.05f, (far - near) * ext);
        float thick = Mathf.Clamp(size.y * 0.6f, 0.15f, 0.35f);
        limb.localPosition = new Vector3(near + len * 0.5f, offset.y, 0f);
        limb.localScale = new Vector3(len, thick, 0.25f);
        limbR.material.color = c;
    }
}
