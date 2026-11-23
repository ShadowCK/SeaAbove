using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Minimal world-space HUD built from TextMesh + quads (no TMP import required):
    /// gaze-following hint line, the fight gauges (<see cref="FightGauges"/>: a stamina capsule by
    /// the fish and a curved tension gauge by the rod), catch cards, toasts, and the
    /// collection book on the left wrist (toggle with Y).
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public Camera head;
        public FishingRod rod;
        public CollectionManager collection;
        public RoomUnderstanding room;
        public Transform leftHand;
        public SfxBank sfx;
        public FishCarrier carrier;

        Font m_Font;
        TextMesh m_Hint, m_Toast, m_Card, m_Book;
        string m_HintSrc;
        Transform m_HintRoot, m_CardRoot, m_BookRoot;
        float m_ToastTimer, m_CardTimer;
        bool m_BookOpen;
        readonly Queue<string> m_Toasts = new Queue<string>();

        void Start()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_HintRoot = new GameObject("HintRoot").transform;
            m_HintRoot.SetParent(transform, false);
            m_Hint = MakeText("Hint", m_HintRoot, Vector3.zero, 0.0045f, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.95f));
            m_Toast = MakeText("Toast", m_HintRoot, new Vector3(0, 0.12f, 0), 0.006f, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.6f));

            m_CardRoot = new GameObject("CatchCard").transform;
            m_CardRoot.SetParent(transform, false);
            var cardBg = new MeshKit(); cardBg.Box(Vector3.zero, new Vector3(0.42f, 0.26f, 0.004f), new Color(0.08f, 0.15f, 0.35f, 0.85f));
            MeshKit.Spawn("CardBg", cardBg.Build("CardBg"), UDFAssets.Glow_(Color.white), m_CardRoot).transform.localPosition = new Vector3(0, 0, 0.004f);
            m_Card = MakeText("CardText", m_CardRoot, Vector3.zero, 0.0055f, TextAnchor.MiddleCenter, Color.white);
            m_CardRoot.gameObject.SetActive(false);

            m_BookRoot = new GameObject("CollectionBook").transform;
            m_BookRoot.SetParent(leftHand, false);
            m_BookRoot.localPosition = new Vector3(0.02f, 0.09f, 0.05f);
            m_BookRoot.localRotation = Quaternion.Euler(30, 0, 0);
            var bookBg = new MeshKit(); bookBg.Box(new Vector3(0, -0.07f, 0.003f), new Vector3(0.24f, 0.3f, 0.003f), new Color(0.05f, 0.1f, 0.25f, 0.85f));
            MeshKit.Spawn("BookBg", bookBg.Build("BookBg"), UDFAssets.Glow_(Color.white), m_BookRoot);
            m_Book = MakeText("BookText", m_BookRoot, new Vector3(-0.11f, 0.07f, 0), 0.0028f, TextAnchor.UpperLeft, Color.white);
            m_BookRoot.gameObject.SetActive(false);

            // ---- fight gauges: fish stamina next to the fish, line tension next to the rod
            gameObject.AddComponent<FightGauges>().Init(head, rod, m_Font);

            rod.Message += ShowToast;
            rod.Landed += ShowCatchCard;
            if (carrier != null)
            {
                carrier.Stored += ShowCatchCard;
                carrier.Message += ShowToast;
                carrier.Released += (f, fresh) =>
                {
                    if (fresh) ShowCatchCard(f);
                    ShowToast($"Released the {f.Species.displayName}. Back to the sea above!");
                };
            }
            collection.Unlocked += msg => { ShowToast(msg); sfx?.Play(SfxBank.Unlock, head.transform.position); };
            collection.RodChanged += r => ShowToast($"Equipped: {r.displayName}");
        }

        TextMesh MakeText(string name, Transform parent, Vector3 pos, float size, TextAnchor anchor, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = m_Font;
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.color = c;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m_Font.material;
            mr.sortingOrder = 10;
            return tm;
        }

        void ShowToast(string msg) { m_Toasts.Enqueue(msg); }

        /// <summary>Word-wraps plain text (TextMesh has no wrapping) at about <paramref name="width"/> characters.</summary>
        static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width) return text;
            var sb = new StringBuilder(text.Length + 8);
            foreach (var para in text.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                int lineLen = 0;
                foreach (var word in para.Split(' '))
                {
                    if (lineLen > 0 && lineLen + 1 + word.Length > width) { sb.Append('\n'); lineLen = 0; }
                    else if (lineLen > 0) { sb.Append(' '); lineLen++; }
                    sb.Append(word);
                    lineLen += word.Length;
                }
            }
            return sb.ToString();
        }

        void ShowCatchCard(FishAgent fish)
        {
            var sp = fish.Species;
            int count = collection.Data.Count(sp.id);
            string rc = ColorUtility.ToHtmlStringRGB(FishSpecies.RarityColor(sp.rarity));
            m_Card.richText = true;
            m_Card.text = $"{(count == 1 ? "<color=#FFD54A>NEW!</color>\n" : "")}<size=90>{sp.displayName}</size>\n" +
                          $"<color=#{rc}>[{FishSpecies.RarityName(sp.rarity)}]</color>  +{sp.coins} coins\n<size=40>{Wrap(sp.description, 44)}</size>";
            m_CardTimer = 4f;
            m_CardRoot.gameObject.SetActive(true);
            Vector3 fwd = head.transform.forward.Flat();
            m_CardRoot.position = head.transform.position + fwd * 0.75f + Vector3.up * 0.05f;
            m_CardRoot.rotation = Quaternion.LookRotation(fwd);
            m_CardRoot.localScale = Vector3.one * 0.01f;
        }

        void Update()
        {
            if (head == null) return;
            Transform h = head.transform;
            Vector3 eye = h.position;
            {
                // lazy-follow in front of the gaze, slightly below its centre (works looking up too)
                Vector3 right = h.right; right.y = 0;
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                right.Normalize();
                Vector3 target = eye + Quaternion.AngleAxis(16f, right) * h.forward * 1.1f;
                m_HintRoot.position = Vector3.Lerp(m_HintRoot.position, target, Time.deltaTime * 3f);
                m_HintRoot.rotation = Quaternion.Slerp(m_HintRoot.rotation, Quaternion.LookRotation(m_HintRoot.position - eye, h.up), Time.deltaTime * 5f);
            }

            string roomMsg = room.CurrentStatus == RoomUnderstanding.Status.Ready ? "" : room.StatusMessage;
            string carryHint = carrier != null ? carrier.Hint : "";
            string hint = !string.IsNullOrEmpty(roomMsg) ? roomMsg : !string.IsNullOrEmpty(carryHint) ? carryHint : rod.Hint;
            if (hint != m_HintSrc) { m_HintSrc = hint; m_Hint.text = Wrap(hint, 46); }

            // toasts
            m_ToastTimer -= Time.deltaTime;
            if (m_ToastTimer <= 0)
            {
                m_Toast.text = "";
                if (m_Toasts.Count > 0) { m_Toast.text = Wrap(m_Toasts.Dequeue(), 40); m_ToastTimer = 2.5f; }
            }

            // catch card pop
            if (m_CardTimer > 0)
            {
                m_CardTimer -= Time.deltaTime;
                float s = Mathf.SmoothStep(0, 1, Mathf.Clamp01((4f - m_CardTimer) * 5f)) * Mathf.Clamp01(m_CardTimer * 4f);
                m_CardRoot.localScale = Vector3.one * Mathf.Max(0.01f, s);
                if (m_CardTimer <= 0) m_CardRoot.gameObject.SetActive(false);
            }

            // collection book
            if (XRInput.SecondaryDown(Hand.Left)) m_BookOpen = !m_BookOpen;
            if (XRInput.PrimaryDown(Hand.Left)) { room.Rescan(); ShowToast("Rescanning your room..."); }
            m_BookRoot.gameObject.SetActive(m_BookOpen);
            if (m_BookOpen) m_Book.text = BuildBookText();
        }

        string BuildBookText()
        {
            var d = collection.Data;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>Fish Log</b>  {d.caught.Count}/{FishSpecies.All.Count}    Coins {d.coins}");
            sb.AppendLine($"Rod: {collection.CurrentRod.displayName}   (right B to switch)");
            sb.AppendLine();
            foreach (var sp in FishSpecies.All)
            {
                int n = d.Count(sp.id);
                string rc = ColorUtility.ToHtmlStringRGB(FishSpecies.RarityColor(sp.rarity));
                sb.AppendLine(n > 0 ? $"<color=#{rc}>●</color> {sp.displayName} x{n}" : $"<color=#{rc}>○</color> ???");
            }
            sb.AppendLine();
            sb.AppendLine("<b>Decorations</b>");
            foreach (var dec in CollectionManager.Decor)
                sb.AppendLine(d.unlockedDecor.Contains(dec.id) ? $"+ {dec.displayName}" : $"[Locked] {dec.requirement}");
            sb.AppendLine();
            sb.AppendLine("Left X: rescan room");
            return sb.ToString();
        }
    }
}
