using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Key bindings plus buffered input for one player. Both players share one keyboard.
[Serializable]
public class FighterControls
{
    public Key left = Key.A;
    public Key right = Key.D;
    public Key up = Key.W;
    public Key down = Key.S;
    public Key light = Key.F;
    public Key heavy = Key.G;
    public Key low = Key.H;
    public Key special = Key.R;

    [NonSerialized] public bool holdLeft, holdRight, holdUp, holdDown;
    [NonSerialized] public int bufLight, bufHeavy, bufLow, bufSpecial;

    bool pendLight, pendHeavy, pendLow, pendSpecial;

    public FighterControls() { }

    public FighterControls(Key left, Key right, Key up, Key down, Key light, Key heavy, Key low, Key special)
    {
        this.left = left;
        this.right = right;
        this.up = up;
        this.down = down;
        this.light = light;
        this.heavy = heavy;
        this.low = low;
        this.special = special;
    }

    // Called every rendered frame so presses between simulation ticks are never lost.
    public void Poll(Keyboard kb)
    {
        if (kb == null) return;
        pendLight |= kb[light].wasPressedThisFrame;
        pendHeavy |= kb[heavy].wasPressedThisFrame;
        pendLow |= kb[low].wasPressedThisFrame;
        pendSpecial |= kb[special].wasPressedThisFrame;
    }

    // Called once per simulation tick. Buffers don't decay during hitstop/freeze.
    public void Step(Keyboard kb, int bufferFrames, bool decay)
    {
        holdLeft = kb != null && kb[left].isPressed;
        holdRight = kb != null && kb[right].isPressed;
        holdUp = kb != null && kb[up].isPressed;
        holdDown = kb != null && kb[down].isPressed;
        bufLight = Latch(ref pendLight, bufLight, bufferFrames, decay);
        bufHeavy = Latch(ref pendHeavy, bufHeavy, bufferFrames, decay);
        bufLow = Latch(ref pendLow, bufLow, bufferFrames, decay);
        bufSpecial = Latch(ref pendSpecial, bufSpecial, bufferFrames, decay);
    }

    static int Latch(ref bool pending, int buffer, int bufferFrames, bool decay)
    {
        if (pending)
        {
            pending = false;
            return bufferFrames;
        }
        return decay ? Mathf.Max(0, buffer - 1) : buffer;
    }

    // Returns the most recently pressed allowed attack still in the buffer, and consumes it.
    public AttackType ConsumeAttack(bool allowLight, bool allowHeavy, bool allowLow)
    {
        int best = 0;
        AttackType result = AttackType.None;
        if (allowLight && bufLight > best) { best = bufLight; result = AttackType.Light; }
        if (allowHeavy && bufHeavy > best) { best = bufHeavy; result = AttackType.Heavy; }
        if (allowLow && bufLow > best) { result = AttackType.Low; }

        if (result == AttackType.Light) bufLight = 0;
        else if (result == AttackType.Heavy) bufHeavy = 0;
        else if (result == AttackType.Low) bufLow = 0;
        return result;
    }

    public void Clear()
    {
        holdLeft = holdRight = holdUp = holdDown = false;
        bufLight = bufHeavy = bufLow = bufSpecial = 0;
        pendLight = pendHeavy = pendLow = pendSpecial = false;
    }
}
