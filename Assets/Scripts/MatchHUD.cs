using UnityEngine;

// Immediate-mode HUD. Deliberately plain: the point is to read the flux mechanic, not to look good.
[RequireComponent(typeof(MatchManager))]
public class MatchHUD : MonoBehaviour
{
    MatchManager m;
    GUIStyle style;
    readonly float[] trail = { 1f, 1f };

    static readonly Color Outline = new Color(0f, 0f, 0f, 0.85f);
    static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f, 0.9f);
    static readonly Color FluxColor = new Color(0.62f, 0.35f, 1f);
    static readonly Color Muted = new Color(0.75f, 0.75f, 0.8f);

    void Awake()
    {
        m = GetComponent<MatchManager>();
    }

    void OnGUI()
    {
        if (m == null || m.config == null || m.player1 == null || m.player2 == null || m.player1.cfg == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, clipping = TextClipping.Overflow };

        float s = Screen.height / 720f;
        Camera cam = Camera.main;

        if (m.freezeFrames > 0) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.1f, 0f, 0.15f, 0.45f));
        if (m.showHitboxes && cam != null) DrawBoxes(cam, s);
        DrawHealth(s);
        DrawFlux(m.player1, 0, s);
        DrawFlux(m.player2, 1, s);
        DrawCombos(s);
        if (cam != null) DrawPopups(cam, s);
        DrawBanner(s);
        DrawFooter(s);
        if (m.phase == MatchManager.Phase.MatchOver) DrawResults(s);
        else if (m.paused) DrawPause(s);
    }

    // ---------------------------------------------------------------- top bar

    void DrawHealth(float s)
    {
        float w = Screen.width;
        float barW = w * 0.38f, barH = 26f * s, top = 24f * s, margin = 24f * s;
        for (int i = 0; i < 2; i++)
        {
            Fighter f = i == 0 ? m.player1 : m.player2;
            bool left = i == 0;
            float ratio = Mathf.Clamp01(f.health / (float)m.config.maxHealth);
            if (ratio > trail[i]) trail[i] = ratio;
            else if (f.comboHits == 0) trail[i] = Mathf.MoveTowards(trail[i], ratio, Time.unscaledDeltaTime * 0.5f);

            float x = left ? margin : w - margin - barW;
            Rect bar = new Rect(x, top, barW, barH);
            Fill(Expand(bar, 3f * s), Outline);
            Fill(bar, BarBg);
            Fill(Portion(bar, trail[i], left), new Color(0.8f, 0.15f, 0.1f));
            Fill(Portion(bar, ratio, left), Color.Lerp(new Color(1f, 0.3f, 0.1f), new Color(1f, 0.85f, 0.2f), ratio));

            Text(new Rect(x, bar.yMax + 4f * s, barW, 24f * s), left ? "P1" : "P2", 20f * s, f.baseColor,
                left ? TextAnchor.UpperLeft : TextAnchor.UpperRight);

            for (int k = 0; k < m.roundsToWin; k++)
            {
                float px = left ? bar.xMax - (k + 1) * 22f * s : bar.x + k * 22f * s + 6f * s;
                Rect pip = new Rect(px, bar.yMax + 8f * s, 16f * s, 16f * s);
                Fill(Expand(pip, 2f), Outline);
                Fill(pip, m.wins[i] > k ? new Color(1f, 0.85f, 0.2f) : BarBg);
            }
        }

        Rect tr = new Rect(w * 0.5f - 44f * s, top - 8f * s, 88f * s, 50f * s);
        Fill(tr, Outline);
        string t = m.trainingMode ? "--" : Mathf.CeilToInt(m.timerFrames / 60f).ToString();
        Text(tr, t, 34f * s, Color.white, TextAnchor.MiddleCenter);
        if (m.trainingMode)
            Text(new Rect(w * 0.5f - 100f * s, tr.yMax + 2f * s, 200f * s, 20f * s), "TRAINING", 14f * s,
                new Color(0.4f, 1f, 0.9f), TextAnchor.UpperCenter);
    }

    // ---------------------------------------------------------------- flux

    void DrawFlux(Fighter f, int i, float s)
    {
        float w = Screen.width, h = Screen.height;
        float barW = w * 0.3f, barH = 20f * s, margin = 24f * s;
        float y = h - 78f * s;
        bool left = i == 0;
        float x = left ? margin : w - margin - barW;
        float ratio = Mathf.Clamp01(f.flux / m.config.maxFlux);
        bool full = f.IsFluxFull;
        FighterControls c = left ? m.p1Controls : m.p2Controls;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f);
        Color superColor = AttackData.TypeColor(AttackType.Super);

        string title = full ? "FLUX FULL - press " + KeyName(c.special) + " to RUSH" : "FLUX " + Mathf.FloorToInt(ratio * 100f) + "%";
        Color titleColor = full ? Color.Lerp(superColor, Color.white, pulse) : FluxColor;
        Text(new Rect(x, y - 26f * s, barW, 24f * s), title, 18f * s, titleColor, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight);

        Rect bar = new Rect(x, y, barW, barH);
        Fill(Expand(bar, 3f * s), full ? titleColor : Outline);
        Fill(bar, BarBg);
        Fill(Portion(bar, ratio, left), full ? Color.Lerp(superColor, Color.white, 0.3f * pulse) : FluxColor);
        for (int k = 1; k < 4; k++) Fill(new Rect(bar.x + bar.width * k / 4f - 1f, bar.y, 2f, bar.height), Outline);

        // Which attack types will earn flux next (the last one you landed won't).
        string chips = Chip("LIGHT", AttackType.Light, f) + "   " + Chip("HEAVY", AttackType.Heavy, f) + "   " + Chip("LOW", AttackType.Low, f);
        Text(new Rect(x, bar.yMax + 4f * s, barW, 22f * s), "<color=#9a9aa8>next flux:</color>   " + chips, 15f * s, Color.white,
            left ? TextAnchor.UpperLeft : TextAnchor.UpperRight, false);
    }

    static string Chip(string label, AttackType t, Fighter f)
    {
        if (f.lastConnectedType == t) return "<color=#4a4a55>" + label + "</color>";
        return "<color=#" + ColorUtility.ToHtmlStringRGB(AttackData.TypeColor(t)) + ">" + label + "</color>";
    }

    // ---------------------------------------------------------------- transient text

    void DrawCombos(float s)
    {
        for (int i = 0; i < 2; i++)
        {
            if (m.comboShowTime[i] <= 0f || m.comboShow[i] < 2) continue;
            float a = Mathf.Clamp01(m.comboShowTime[i] / 0.3f);
            Rect r = new Rect(i == 0 ? 30f * s : Screen.width - 330f * s, Screen.height * 0.28f, 300f * s, 60f * s);
            Text(r, m.comboShow[i] + " HITS", 40f * s, new Color(1f, 0.9f, 0.3f, a), i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
        }
    }

    void DrawPopups(Camera cam, float s)
    {
        foreach (MatchManager.FloatingText p in m.popups)
        {
            Vector3 sp = cam.WorldToScreenPoint(p.world + Vector3.up * p.age * 0.8f);
            if (sp.z < 0f) continue;
            Color c = p.color;
            c.a = Mathf.Clamp01((p.life - p.age) / (p.life * 0.35f));
            Text(new Rect(sp.x - 160f * s, Screen.height - sp.y - 14f * s, 320f * s, 28f * s), p.text, 18f * s, c, TextAnchor.MiddleCenter);
        }
    }

    void DrawBanner(float s)
    {
        if (m.bannerFrames <= 0 || string.IsNullOrEmpty(m.bannerText)) return;
        Rect r = new Rect(0f, Screen.height * 0.32f, Screen.width, 90f * s);
        Fill(new Rect(0f, r.y + 5f * s, Screen.width, r.height - 10f * s), new Color(0f, 0f, 0f, 0.45f));
        Text(r, m.bannerText, 60f * s, Color.white, TextAnchor.MiddleCenter);
    }

    void DrawFooter(float s)
    {
        float w = Screen.width, h = Screen.height;
        Rect r = new Rect(24f * s, h - 24f * s, w - 48f * s, 20f * s);
        Text(r, KeysLine(m.p1Controls), 13f * s, Muted, TextAnchor.LowerLeft);
        Text(r, KeysLine(m.p2Controls), 13f * s, Muted, TextAnchor.LowerRight);
        Text(new Rect(0f, h - 78f * s, w, 20f * s), "Esc pause  ·  F1 hitboxes  ·  F2 training  ·  F3 fill flux  ·  Backspace restart",
            13f * s, Muted, TextAnchor.UpperCenter);
    }

    static string KeysLine(FighterControls c)
    {
        return KeyName(c.left) + "/" + KeyName(c.right) + " move   " + KeyName(c.up) + " jump   " + KeyName(c.down) + " crouch    "
               + KeyName(c.light) + " light   " + KeyName(c.heavy) + " heavy   " + KeyName(c.low) + " low   " + KeyName(c.special) + " rush";
    }

    static string KeyName(UnityEngine.InputSystem.Key k)
    {
        return k.ToString().Replace("Numpad", "Num").Replace("Arrow", "");
    }

    // ---------------------------------------------------------------- overlays

    void DrawResults(float s)
    {
        float w = 620f * s, h = 380f * s;
        Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        Fill(r, new Color(0f, 0f, 0f, 0.85f));
        Text(new Rect(r.x, r.y + 14f * s, w, 50f * s), m.matchResultText, 40f * s, Color.white, TextAnchor.UpperCenter);

        FighterStats a = m.player1.stats, b = m.player2.stats;
        string[,] rows =
        {
            { "", "P1", "P2" },
            { "Attacks landed (hit or blocked)", a.connects.ToString(), b.connects.ToString() },
            { "Alternated (earned flux)", Pct(a.alternations, a.connects), Pct(b.alternations, b.connects) },
            { "Flux earned", Mathf.RoundToInt(a.fluxGained).ToString(), Mathf.RoundToInt(b.fluxGained).ToString() },
            { "Rush used", a.supersUsed.ToString(), b.supersUsed.ToString() },
            { "Rush hit / blocked", a.supersLanded + " / " + a.supersBlocked, b.supersLanded + " / " + b.supersBlocked },
            { "Rush damage", a.superDamage.ToString(), b.superDamage.ToString() },
            { "Total damage", a.damageDealt.ToString(), b.damageDealt.ToString() },
        };
        float rowH = 30f * s, y = r.y + 80f * s;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            Color c = i == 0 ? Muted : Color.white;
            Text(new Rect(r.x + 30f * s, y, 330f * s, rowH), rows[i, 0], 17f * s, Muted, TextAnchor.MiddleLeft);
            Text(new Rect(r.x + 370f * s, y, 100f * s, rowH), rows[i, 1], 17f * s, i == 0 ? m.player1.baseColor : c, TextAnchor.MiddleCenter);
            Text(new Rect(r.x + 480f * s, y, 100f * s, rowH), rows[i, 2], 17f * s, i == 0 ? m.player2.baseColor : c, TextAnchor.MiddleCenter);
            y += rowH;
        }
        Text(new Rect(r.x, r.yMax - 40f * s, w, 30f * s), "Enter / Space: rematch", 18f * s, new Color(1f, 0.85f, 0.2f), TextAnchor.MiddleCenter);
    }

    static string Pct(int part, int whole) => whole == 0 ? "-" : Mathf.RoundToInt(100f * part / whole) + "%";

    void DrawPause(float s)
    {
        float w = 820f * s, h = 470f * s;
        Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        Fill(r, new Color(0f, 0f, 0f, 0.88f));
        Text(new Rect(r.x, r.y + 12f * s, w, 44f * s), "PAUSED", 34f * s, Color.white, TextAnchor.UpperCenter);

        string[] lines =
        {
            "<b><color=#c890ff>FLUX</color></b>",
            "Land an attack (hit or block) that is a DIFFERENT type than the last one you landed: gain flux.",
            "Repeating the same attack type gives nothing. Blocked attacks give half.",
            "Full bar: press Rush. A fast dash; on hit it's a 5-hit combo, on block or whiff you're wide open.",
            "",
            "<b><color=#ffd84a>BASICS</color></b>",
            "Hold back to block. LIGHT is blocked high or low, HEAVY is an overhead (block standing), LOW must be crouch-blocked.",
            "Jump attacks are overheads. Getting hit in the air knocks you down.",
            "On hit or block, cancel into an attack type you haven't used in that chain (max 3), or into Rush.",
            "",
            "<b>P1</b>   " + KeysLine(m.p1Controls),
            "<b>P2</b>   " + KeysLine(m.p2Controls),
            "",
            "Esc / P resume   ·   F1 hitboxes   ·   F2 training (no timer, auto-heal)   ·   F3 fill flux   ·   Backspace restart",
        };
        float y = r.y + 70f * s;
        foreach (string line in lines)
        {
            Text(new Rect(r.x + 30f * s, y, w - 60f * s, 24f * s), line, 15f * s, Color.white, TextAnchor.MiddleLeft, false);
            y += line.Length == 0 ? 10f * s : 26f * s;
        }
    }

    void DrawBoxes(Camera cam, float s)
    {
        foreach (Fighter f in new[] { m.player1, m.player2 })
        {
            Rect hurt = ToGUI(cam, f.Hurtbox);
            Frame(hurt, f.CanBeHit ? new Color(0.2f, 1f, 0.3f) : new Color(0.6f, 0.6f, 0.6f), 2f);
            if (f.TryGetHitbox(out Rect hb, out _)) Fill(ToGUI(cam, hb), new Color(1f, 0.1f, 0.1f, 0.45f));
            if (f.TryGetSuperHitbox(out Rect sb)) Fill(ToGUI(cam, sb), new Color(1f, 0.2f, 0.9f, 0.45f));
            Text(new Rect(hurt.center.x - 100f * s, hurt.yMax + 2f * s, 200f * s, 18f * s), f.state.ToString(), 12f * s, Color.white, TextAnchor.UpperCenter);
        }
    }

    // ---------------------------------------------------------------- helpers

    static Rect ToGUI(Camera cam, Rect world)
    {
        Vector3 a = cam.WorldToScreenPoint(new Vector3(world.xMin, world.yMin, 0f));
        Vector3 b = cam.WorldToScreenPoint(new Vector3(world.xMax, world.yMax, 0f));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
    }

    static Rect Portion(Rect bar, float ratio, bool anchorLeft)
    {
        float pw = bar.width * Mathf.Clamp01(ratio);
        return anchorLeft ? new Rect(bar.x, bar.y, pw, bar.height) : new Rect(bar.xMax - pw, bar.y, pw, bar.height);
    }

    static Rect Expand(Rect r, float a) => new Rect(r.x - a, r.y - a, r.width + 2f * a, r.height + 2f * a);

    static void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void Frame(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void Text(Rect r, string text, float size, Color c, TextAnchor anchor, bool shadow = true)
    {
        style.fontSize = Mathf.Max(8, Mathf.RoundToInt(size));
        style.alignment = anchor;
        style.fontStyle = FontStyle.Bold;
        if (shadow)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.85f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        }
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }
}
