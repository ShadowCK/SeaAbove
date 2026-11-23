using System.Collections.Generic;
using UnityEngine;

namespace UpsideDownFishing
{
    public enum FightPhase { Run, Tug, Surface, LastStand, Tired }
    public enum FishMove { Steady, Rush, Rest, SideRun, HeadShake, Jump }

    /// <summary>
    /// The hooked fish's "brain" during a fight. Pure logic: FishingRod feeds it the player's
    /// actions each frame and applies the resulting pull / position / feedback.
    ///
    /// Phases: Run (opening dash) → Tug (moves, stamina 1→0.4) → Surface (moves near the
    /// surface, 0.4→0.1) → LastStand (one final dash) → Tired (reel it in).
    ///
    /// Moves and their counters (the player reads them from line, rod, haptics, splashes):
    ///   Rush      — stop cranking, hold the rod up                    (cranking = tension spike)
    ///   Rest      — crank NOW, best time to gain line                 (idle = fish recovers)
    ///   SideRun   — pull the rod to the OPPOSITE side (side pressure) (same side = no effect)
    ///   HeadShake — keep the line tight                               (slack = hook works loose)
    ///   Jump      — "bow": thrust the rod tip toward the fish         (no bow = hook damage + spike)
    ///
    /// Stamina only drops quickly when the player plays well, so duration follows skill; the
    /// tuning targets (seconds of tug+surface) are set per rarity.
    /// </summary>
    public class FishFight
    {
        public struct Input
        {
            public float dt;
            public float effort;          // 0..1 crank speed
            public float tension;         // 0..1+ (1 = line breaking strain)
            public float pumping;         // 0..1 rod pulled down (away from the fish)
            public float sideOffset;      // m, rod tip lateral displacement since the move began (+ = player's right)
            public float towardFish;      // m/s, rod tip speed toward the fish
            public bool cranking;         // hand is on the crank
        }

        public readonly FishSpecies Species;
        public readonly float TargetSeconds;
        public FightPhase Phase { get; private set; } = FightPhase.Run;
        public FishMove Move { get; private set; } = FishMove.Steady;
        public float MoveTime { get; private set; }
        public float MoveDuration { get; private set; }
        public float Stamina { get; private set; } = 1f;
        public float HookHold { get; private set; } = 1f;
        public int SideDir { get; private set; } = 1;          // +1 = toward the player's right
        public bool CounterOk { get; private set; } = true;
        public int Perfects { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>Jump: wind-up (telegraph) then airborne. JumpAir 0..1 while out of the water.</summary>
        public const float JumpWindup = 0.8f, JumpAirTime = 1.0f;
        /// <summary>Below this tension the line counts as slack (the hook starts working loose).</summary>
        public const float SlackTension = 0.12f;
        public bool JumpTelegraph => Move == FishMove.Jump && MoveTime < JumpWindup;
        public float JumpAir => Move == FishMove.Jump && MoveTime >= JumpWindup ? Mathf.Clamp01((MoveTime - JumpWindup) / JumpAirTime) : -1f;

        /// <summary>Raised on start of a move / when a move ends (move, countered well).</summary>
        public System.Action<FishMove> MoveStarted;
        public System.Action<FishMove, bool> MoveEnded;
        /// <summary>Jump finished without the player bowing: rod should take a tension spike.</summary>
        public System.Action JumpPenalty;

        readonly List<FishMove> m_Special = new List<FishMove>();
        FishMove m_LastMove = FishMove.Steady;
        float m_GoodTime, m_BadTime;
        bool m_Bowed;
        /// <summary>Beginner fish: the hook barely works loose (multiplies hook-hold losses).</summary>
        readonly float m_HookLoss = 1f;

        static float TargetFor(Rarity r) => r switch
        {
            // stamina pool in "seconds at baseline drain". Calibrated with a headless simulation so a
            // player who reads the fish lands it in (incl. ~3 s final reel-in): common 10-17 s,
            // uncommon 17-23 s, rare 27-33 s, legendary 40-60 s. Ignoring the moves takes much longer.
            Rarity.Common => 9f,
            Rarity.Uncommon => 19f,
            Rarity.Rare => 36f,
            _ => 85f,
        };

        public FishFight(FishSpecies sp)
        {
            Species = sp;
            TargetSeconds = (sp.beginner ? 4.5f : TargetFor(sp.rarity)) * Random.Range(0.9f, 1.1f);
            if (sp.beginner) m_HookLoss = 0.2f;
            switch (sp.rarity)
            {
                case Rarity.Common: break;
                case Rarity.Uncommon: m_Special.Add(FishMove.HeadShake); m_Special.Add(FishMove.SideRun); break;
                case Rarity.Rare:
                    m_Special.Add(FishMove.Jump);
                    m_Special.Add(Random.value < 0.5f ? FishMove.HeadShake : FishMove.SideRun);
                    break;
                default: m_Special.AddRange(new[] { FishMove.HeadShake, FishMove.SideRun, FishMove.Jump }); break;
            }
            // opening dash: the longer the fight, the longer the first run
            Begin(FishMove.Rush, sp.beginner ? 0.9f : Mathf.Lerp(1.4f, 2.8f, Mathf.InverseLerp(9f, 85f, TargetSeconds)));
        }

        public bool Allows(FishMove m) => m == FishMove.Rush || m == FishMove.Rest || m == FishMove.Steady || m_Special.Contains(m);

        /// <summary>How hard the fish pulls, as a multiple of species strength.</summary>
        public float PullFactor
        {
            get
            {
                if (Phase == FightPhase.Tired) return 0.1f;
                switch (Move)
                {
                    case FishMove.Rush: return Phase == FightPhase.Run ? 1.35f : 1.25f;
                    case FishMove.Rest: return 0.12f;
                    case FishMove.SideRun: return 0.85f;
                    case FishMove.HeadShake: return 0.55f;
                    case FishMove.Jump: return JumpAir >= 0 ? 0.25f : 0.7f;
                    default: return 0.45f;
                }
            }
        }

        /// <summary>0..1 rhythmic tension pulses during a head-shake.</summary>
        public float ShakePulse => Move == FishMove.HeadShake ? Mathf.Pow(Mathf.Abs(Mathf.Sin(MoveTime * 22f)), 3f) : 0f;

        public void Update(Input i)
        {
            float dt = i.dt;
            Elapsed += dt;
            MoveTime += dt;
            if (Phase == FightPhase.Tired)
            {
                // a tired fish can still shake the hook if the line is left slack
                Move = FishMove.Steady;
                if (i.tension < SlackTension && !(i.cranking && i.effort > 0.25f)) HookHold = Mathf.Max(0f, HookHold - 0.15f * m_HookLoss * dt);
                return;
            }

            // ---- judge the player's response to the current move (hand must be on the reel)
            float perf = 0.8f;
            switch (Move)
            {
                case FishMove.Rush:
                    CounterOk = i.cranking && i.effort < 0.35f;
                    perf = CounterOk ? 1.15f : 0.5f;
                    break;
                case FishMove.Rest:
                    CounterOk = i.cranking && i.effort > 0.3f;
                    perf = CounterOk ? 1.6f : 0.25f;
                    if (!CounterOk && MoveTime > 1f) Stamina = Mathf.Min(1f, Stamina + 0.02f * dt);   // it catches its breath
                    break;
                case FishMove.SideRun:
                    CounterOk = i.sideOffset * SideDir < -0.08f;
                    perf = CounterOk ? 2.2f : 0.35f;
                    break;
                case FishMove.HeadShake:
                    CounterOk = i.tension > 0.22f || (i.cranking && i.effort > 0.25f);   // keep it tight: reel steadily
                    perf = CounterOk ? 1.4f : 0.4f;
                    if (!CounterOk) HookHold -= 0.45f * m_HookLoss * dt;
                    break;
                case FishMove.Jump:
                    if (JumpAir >= 0 && i.towardFish > 0.6f) m_Bowed = true;
                    CounterOk = JumpAir < 0 || m_Bowed;
                    perf = 0.6f;
                    break;
                default:
                    CounterOk = i.cranking;
                    perf = 0.3f + i.effort * 0.6f + i.pumping * 0.9f;
                    break;
            }
            perf += i.pumping * 0.5f;
            if (Species.beginner) perf = Mathf.Max(perf, 0.9f);   // it tires even if the player is still learning
            if (CounterOk) m_GoodTime += dt; else m_BadTime += dt;

            // ---- stamina: drains with good play AND line pressure; the opening dash costs nothing
            if (Phase != FightPhase.Run)
            {
                float drain = perf * (0.25f + 0.9f * Mathf.Clamp01(i.tension)) / TargetSeconds;
                Stamina = Mathf.Max(0f, Stamina - drain * dt);
            }

            // ---- hook hold: slack lets the hook work loose; steady pressure seats it again
            bool airborne = JumpAir >= 0;
            bool slack = i.tension < SlackTension && !(i.cranking && i.effort > 0.25f);   // reeling keeps it taut
            if (slack && Move != FishMove.Rest && !airborne) HookHold -= 0.3f * m_HookLoss * dt;
            else if (i.tension > 0.25f && i.tension < 0.85f) HookHold = Mathf.Min(1f, HookHold + 0.04f * dt);
            HookHold = Mathf.Max(0f, HookHold);

            if (MoveTime >= MoveDuration) EndMove();
        }

        void EndMove()
        {
            var m = Move;
            bool good = m_GoodTime >= m_BadTime;
            if (m == FishMove.Jump)
            {
                good = m_Bowed;
                if (!m_Bowed) { HookHold -= 0.45f * m_HookLoss; JumpPenalty?.Invoke(); }
            }
            if (good && (m == FishMove.SideRun || m == FishMove.HeadShake || m == FishMove.Jump || m == FishMove.Rush))
            {
                Stamina = Mathf.Max(0f, Stamina - 0.04f);
                if (m != FishMove.Rush) Perfects++;
            }
            MoveEnded?.Invoke(m, good);

            // ---- phase transitions
            if (Phase == FightPhase.Run) Phase = FightPhase.Tug;
            if (Phase == FightPhase.Tug && Stamina <= 0.4f) Phase = FightPhase.Surface;
            if (Phase == FightPhase.Surface && Stamina <= 0.1f)
            {
                Phase = FightPhase.LastStand;
                Begin(FishMove.Rush, Random.Range(1.0f, 1.6f));
                return;
            }
            if (Phase == FightPhase.LastStand) { Phase = FightPhase.Tired; Stamina = 0f; Begin(FishMove.Steady, 999f); return; }
            if (Phase == FightPhase.Tug && Stamina <= 0f) { Phase = FightPhase.Tired; Begin(FishMove.Steady, 999f); return; }
            PickNext();
        }

        void PickNext()
        {
            var w = new List<(FishMove, float)>();
            bool afterEffort = m_LastMove == FishMove.Rush || m_LastMove == FishMove.SideRun || m_LastMove == FishMove.Jump;
            if (Phase == FightPhase.Tug)
            {
                w.Add((FishMove.Rush, Species.beginner ? 1f : 3f));
                w.Add((FishMove.Rest, afterEffort ? 4f : 1.5f));
                w.Add((FishMove.Steady, 2f));
                if (Allows(FishMove.SideRun)) w.Add((FishMove.SideRun, 2.5f));
                if (Allows(FishMove.HeadShake)) w.Add((FishMove.HeadShake, 1f));
            }
            else // Surface
            {
                w.Add((FishMove.Rest, afterEffort ? 3f : 1.5f));
                w.Add((FishMove.Rush, 1f));
                w.Add((FishMove.Steady, 1f));
                if (Allows(FishMove.HeadShake)) w.Add((FishMove.HeadShake, 3f));
                if (Allows(FishMove.Jump)) w.Add((FishMove.Jump, 2.5f));
                if (Allows(FishMove.SideRun)) w.Add((FishMove.SideRun, 1f));
            }
            // no immediate repeats of special moves
            for (int k = 0; k < w.Count; k++)
                if (w[k].Item1 == m_LastMove && m_LastMove != FishMove.Steady) w[k] = (w[k].Item1, w[k].Item2 * 0.2f);

            float sum = 0; foreach (var e in w) sum += e.Item2;
            float r = Random.value * sum;
            var pick = FishMove.Steady;
            foreach (var e in w) { r -= e.Item2; if (r <= 0) { pick = e.Item1; break; } }

            float dur = pick switch
            {
                FishMove.Rush => Random.Range(1.2f, 2.2f),
                FishMove.Rest => Random.Range(1.4f, 2.2f),
                FishMove.SideRun => Random.Range(1.6f, 2.4f),
                FishMove.HeadShake => Random.Range(1.0f, 1.6f),
                FishMove.Jump => JumpWindup + JumpAirTime,
                _ => Random.Range(1.0f, 1.8f),
            };
            Begin(pick, dur);
        }

        void Begin(FishMove m, float duration)
        {
            Move = m;
            m_LastMove = m;
            MoveTime = 0;
            MoveDuration = duration;
            m_GoodTime = m_BadTime = 0;
            m_Bowed = false;
            CounterOk = true;
            if (m == FishMove.SideRun) SideDir = Random.value < 0.5f ? -1 : 1;
            MoveStarted?.Invoke(m);
        }
    }
}
