using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// One fish. In the ceiling sea it swims belly-up (the sea is inverted); once landed it
    /// flips upright and floats around the real room, steering around furniture.
    /// </summary>
    public class FishAgent : MonoBehaviour
    {
        public enum State { Swimming, Approaching, Nibbling, Hooked, Landing, Floating, Escaping, Held, Released }

        /// <summary>Every live fish (small list; used to find loose fish near the hand).</summary>
        public static readonly System.Collections.Generic.List<FishAgent> All = new System.Collections.Generic.List<FishAgent>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Out of the sea and free in the room (can be grabbed by hand).</summary>
        public bool IsLoose => CurrentState == State.Landing || CurrentState == State.Floating;

        public FishSpecies Species { get; private set; }
        public State CurrentState { get; private set; }
        public float Stamina { get; set; }

        CeilingWater m_Water;
        RoomModel m_Room;
        Transform m_Head;
        Vector3 m_Target;
        Vector3 m_Velocity;
        float m_Speed;
        float m_RetargetTimer;
        float m_StateTime;
        Vector3 m_UpVector = Vector3.down;
        Vector3 m_LurePos;
        int m_Nibbles;
        float m_EscapeUntil;

        public const int RoomLayer = 30;

        public System.Action<FishAgent> Bit;      // raised when the fish truly bites
        public System.Action<FishAgent> Nibbled;  // small tug

        public void Init(FishSpecies sp, CeilingWater water, Transform head)
        {
            Species = sp;
            m_Water = water;
            m_Room = water.Room;
            m_Head = head;
            Stamina = sp.stamina;
            m_Speed = sp.swimSpeed * Random.Range(0.8f, 1.2f);
            var p = m_Room.RandomPointInside(0.3f);
            float r0 = Random.value;
            transform.position = new Vector3(p.x, Mathf.Lerp(WaterMinY, WaterMaxY, r0 * r0), p.y);
            transform.rotation = Quaternion.LookRotation(Random.onUnitSphere.Flat(), m_UpVector);
            GetComponent<MeshRenderer>().material.SetFloat("_SwimPhase", Random.value * 10f);
            PickWaterTarget();
            SetState(State.Swimming);
        }

        float WaterMinY => m_Water.SurfaceY + 0.12f + Species.length * 0.3f;
        // stay in the bright band under the surface; deeper they would vanish into the blue
        float WaterMaxY => Mathf.Max(WaterMinY + 0.2f, m_Water.SurfaceY + CeilingWater.ShallowBand - Species.length * 0.3f);

        void SetState(State s) { CurrentState = s; m_StateTime = 0; }

        public bool CanBeAttracted => CurrentState == State.Swimming && Time.time > m_EscapeUntil;

        public void AttractTo(Vector3 lure)
        {
            m_LurePos = lure;
            m_Nibbles = Species.beginner ? Random.Range(0, 2) : Random.Range(1, 4);
            SetState(State.Approaching);
        }

        public void LoseInterest(float cooldown = 4f)
        {
            if (CurrentState == State.Approaching || CurrentState == State.Nibbling)
            {
                m_EscapeUntil = Time.time + cooldown;
                SetState(State.Swimming);
                PickWaterTarget();
            }
        }

        public void Hook() => SetState(State.Hooked);

        /// <summary>Fish broke free during the fight: dart away.</summary>
        public void Escape()
        {
            m_EscapeUntil = Time.time + 10f;
            Stamina = Species.stamina;
            SetState(State.Escaping);
            PickWaterTarget();
            m_Speed = Species.swimSpeed * 2.5f;
        }

        /// <summary>Called by the rod while hooked: the fight logic owns the position.</summary>
        public void DriveHooked(Vector3 pos, Vector3 lookDir, float thrash)
        {
            transform.position = pos;
            if (lookDir.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir, m_UpVector), Time.deltaTime * 8f);
            transform.Rotate(0, Mathf.Sin(Time.time * 25f) * thrash * 25f, 0, Space.Self);
        }

        /// <summary>Taken out of the sim: the line or a hand drives the transform.</summary>
        public void Hold()
        {
            SetState(State.Held);
            m_UpVector = Vector3.up;
            gameObject.layer = 0;
        }

        /// <summary>Slipped out of the hand: tumbles away and then floats around the room.</summary>
        public void ReleaseLoose(Vector3 velocity)
        {
            SetState(State.Landing);
            m_UpVector = Vector3.up;
            m_Velocity = velocity;
        }

        /// <summary>Raised when a released fish reaches the ceiling sea again.</summary>
        public System.Action<FishAgent> ReturnedToSea;
        bool m_ReleaseSplashed;

        /// <summary>Released back to the wild: it floats up out of the room, splashes into the ceiling sea
        /// and swims off with the school (it won't take a lure again for a while).</summary>
        public void Release(Vector3 velocity)
        {
            SetState(State.Released);
            m_UpVector = Vector3.up;
            m_Velocity = velocity;
            m_ReleaseSplashed = false;
            gameObject.layer = 0;
        }

        void UpdateReleased()
        {
            float dt = Time.deltaTime;
            // the ceiling sea draws it home: rise, while the toss drifts out sideways
            m_Velocity += Vector3.up * 3.5f * dt;
            float damp = Mathf.Exp(-1.5f * dt);
            m_Velocity.x *= damp; m_Velocity.z *= damp;
            m_Velocity.y = Mathf.Clamp(m_Velocity.y, -1.5f, 2.2f);
            var pos = transform.position + m_Velocity * dt;
            var xz = m_Room.ClampInside(new Vector2(pos.x, pos.z), 0.1f + Species.length * 0.3f);
            pos = new Vector3(xz.x, pos.y, xz.y);
            transform.position = pos;

            // nose toward where it is going, wriggling like it swims
            Vector3 dir = m_Velocity.sqrMagnitude > 0.01f ? m_Velocity.normalized : Vector3.up;
            Vector3 upHint = Mathf.Abs(dir.y) > 0.9f ? transform.up : (m_ReleaseSplashed ? Vector3.down : Vector3.up);
            if (Mathf.Abs(Vector3.Dot(upHint, dir)) > 0.98f) upHint = transform.right;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, upHint), dt * 5f);
            transform.Rotate(0, Mathf.Sin(m_StateTime * 18f) * 30f * dt, 0, Space.Self);

            if (!m_ReleaseSplashed && pos.y >= m_Water.SurfaceY)
            {
                m_ReleaseSplashed = true;
                var surf = new Vector3(pos.x, m_Water.SurfaceY, pos.z);
                SplashFx.Play(surf, Mathf.Lerp(0.6f, 1.1f, Species.length));
                CeilingWater.AddRipple(surf);
                m_UpVector = Vector3.down;
                m_Velocity *= 0.4f;
                ReturnedToSea?.Invoke(this);
            }
            if ((m_ReleaseSplashed && pos.y >= WaterMinY) || m_StateTime > 6f)
            {
                transform.position = new Vector3(pos.x, Mathf.Clamp(pos.y, WaterMinY, WaterMaxY), pos.z);
                m_UpVector = Vector3.down;
                m_EscapeUntil = Time.time + 15f;
                Stamina = Species.stamina;
                SetState(State.Escaping);
                PickWaterTarget();
                m_Speed = Species.swimSpeed * 2f;
            }
        }

        /// <summary>Pulled out through the surface into the room.</summary>
        public void Land()
        {
            SetState(State.Landing);
            m_UpVector = Vector3.up;
            m_Velocity = Vector3.down * 1.2f + Random.insideUnitSphere * 0.3f;
            gameObject.layer = 0;
        }

        void Update()
        {
            if (m_Room == null) return;
            m_StateTime += Time.deltaTime;
            switch (CurrentState)
            {
                case State.Swimming:
                case State.Escaping:
                    if (CurrentState == State.Escaping && m_StateTime > 2.5f) { m_Speed = Species.swimSpeed; SetState(State.Swimming); }
                    m_RetargetTimer -= Time.deltaTime;
                    if (m_RetargetTimer <= 0 || (m_Target - transform.position).sqrMagnitude < 0.04f) PickWaterTarget();
                    Steer(m_Target, m_Speed, 2.0f);
                    break;

                case State.Approaching:
                {
                    Vector3 ahead = m_LurePos + Vector3.up * (Species.length * 0.6f);
                    Steer(ahead, Species.beginner ? Mathf.Max(m_Speed, 0.35f) * 1.2f : m_Speed * 0.8f, 2.5f);
                    if ((transform.position - ahead).sqrMagnitude < 0.01f) SetState(State.Nibbling);
                    if (m_StateTime > 12f) LoseInterest();
                    break;
                }

                case State.Nibbling:
                {
                    // dart in and out of the lure
                    float phase = Mathf.Repeat(m_StateTime, 1.4f);
                    Vector3 basePos = m_LurePos + Vector3.up * (Species.length * 0.6f);
                    Vector3 toLure = (m_LurePos - basePos).normalized;
                    transform.position = Vector3.Lerp(transform.position, basePos + toLure * (phase < 0.25f ? 0.08f : 0f), Time.deltaTime * 10f);
                    FaceDir(m_LurePos - transform.position, 6f);
                    if (phase < Time.deltaTime * 1.01f && m_StateTime > 0.5f)
                    {
                        if (m_Nibbles-- > 0) Nibbled?.Invoke(this);
                        else Bit?.Invoke(this);
                    }
                    break;
                }

                case State.Hooked:
                    break;

                case State.Released:
                    UpdateReleased();
                    break;

                case State.Landing:
                    // pop out of the water, then decelerate and start floating
                    m_Velocity = Vector3.Lerp(m_Velocity, Vector3.zero, Time.deltaTime * 1.5f);
                    transform.position += m_Velocity * Time.deltaTime;
                    transform.Rotate(Vector3.right, 360f * Time.deltaTime * Mathf.Clamp01(1.2f - m_StateTime), Space.Self);
                    if (m_StateTime > 1.2f) { SetState(State.Floating); m_Speed = Species.swimSpeed * 0.5f; PickRoomTarget(); }
                    break;

                case State.Floating:
                    m_RetargetTimer -= Time.deltaTime;
                    if (m_RetargetTimer <= 0 || (m_Target - transform.position).sqrMagnitude < 0.05f) PickRoomTarget();
                    SteerInRoom();
                    break;
            }
        }

        void PickWaterTarget()
        {
            var p = m_Room.RandomPointInside(0.25f + Species.length * 0.5f);
            // favour the bright shallows right under the surface (where the player sees them)
            float r = Random.value;
            m_Target = new Vector3(p.x, Mathf.Lerp(WaterMinY, WaterMaxY, r * r * r), p.y);
            m_RetargetTimer = Random.Range(3f, 7f);
        }

        void PickRoomTarget()
        {
            for (int i = 0; i < 10; i++)
            {
                var p = m_Room.RandomPointInside(0.4f);
                var t = new Vector3(p.x, Random.Range(m_Room.floorY + 0.6f, m_Room.ceilingY - 0.45f), p.y);
                if (!Physics.CheckSphere(t, 0.25f, 1 << RoomLayer)) { m_Target = t; break; }
            }
            m_RetargetTimer = Random.Range(5f, 10f);
        }

        void Steer(Vector3 target, float speed, float turnRate)
        {
            Vector3 desired = (target - transform.position);
            if (desired.sqrMagnitude > 1e-4f) FaceDir(desired, turnRate);
            transform.position += transform.forward * speed * Time.deltaTime;
            // stay in the sea volume
            var pos = transform.position;
            pos.y = Mathf.Clamp(pos.y, WaterMinY, WaterMaxY);
            var xz = m_Room.ClampInside(new Vector2(pos.x, pos.z), 0.1f + Species.length * 0.3f);
            transform.position = new Vector3(xz.x, pos.y, xz.y);
        }

        void SteerInRoom()
        {
            Vector3 desired = (m_Target - transform.position).normalized;
            // obstacle avoidance against room furniture
            if (Physics.SphereCast(transform.position, Species.length * 0.4f, transform.forward, out var hit, 0.6f, 1 << RoomLayer))
                desired = Vector3.Reflect(transform.forward, hit.normal) + hit.normal;
            // keep a polite distance from the player's head
            if (m_Head != null)
            {
                Vector3 away = transform.position - m_Head.position;
                if (away.magnitude < 0.5f) desired += away.normalized * 2f;
            }
            FaceDir(desired, 1.5f);
            float bob = Mathf.Sin(Time.time * 1.3f + GetInstanceID()) * 0.03f;
            transform.position += (transform.forward * m_Speed + Vector3.up * bob) * Time.deltaTime;
            var pos = transform.position;
            pos.y = Mathf.Clamp(pos.y, m_Room.floorY + 0.3f, m_Room.ceilingY - 0.3f);
            var xz = m_Room.ClampInside(new Vector2(pos.x, pos.z), 0.25f);
            transform.position = new Vector3(xz.x, pos.y, xz.y);
        }

        void FaceDir(Vector3 dir, float rate)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            var look = Quaternion.LookRotation(dir.normalized, m_UpVector);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * rate);
        }
    }

    static class VecExt
    {
        public static Vector3 Flat(this Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }
    }
}
