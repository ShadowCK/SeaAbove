using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UpsideDownFishing
{
    /// <summary>
    /// Minimal world-space HUD built from TextMesh + quads (no TMP import required):
    /// lazy-follow hint line, tension/stamina bars by the rod, catch cards, toasts, and the
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

        Font m_Font;
        TextMesh m_Hint, m_Toast, m_Card, m_Book;
        Transform m_HintRoot, m_CardRoot, m_BookRoot, m_BarRoot;
        Transform m_TensionFill, m_StaminaFill;
        Material m_TensionMat;
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

            // tension / stamina bars above the rod hand
            m_BarRoot = new GameObject("FightBars").transform;
            m_BarRoot.SetParent(transform, false);
            MakeBar("TensionBg", new Vector3(0, 0, 0.001f), new Vector3(0.2f, 0.018f, 0.002f), new Color(0, 0, 0, 0.55f));
            m_TensionFill = MakeBar("TensionFill", Vector3.zero, new Vector3(0.2f, 0.014f, 0.002f), Color.white, out m_TensionMat);
            MakeBar("StaminaBg", new Vector3(0, -0.026f, 0.001f), new Vector3(0.2f, 0.01f, 0.002f), new Color(0, 0, 0, 0.55f));
            m_StaminaFill = MakeBar("StaminaFill", new Vector3(0, -0.026f, 0), new Vector3(0.2f, 0.007f, 0.002f), new Color(0.4f, 0.8f, 1f, 0.95f));
            MakeText("TensionLabel", m_BarRoot, new Vector3(0, 0.025f, 0), 0.0025f, TextAnchor.MiddleCenter, Color.white).text = "张力 / 鱼的体力";
            m_BarRoot.gameObject.SetActive(false);

            rod.Message += ShowToast;
            rod.Landed += ShowCatchCard;
            collection.Unlocked += msg => { ShowToast(msg); sfx?.Play(SfxBank.Unlock, head.transform.position); };
            collection.RodChanged += r => ShowToast($"装备：{r.displayName}");
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

        Transform MakeBar(string name, Vector3 pos, Vector3 size, Color c) => MakeBar(name, pos, size, c, out _);

        Transform MakeBar(string name, Vector3 pos, Vector3 size, Color c, out Material mat)
        {
            var k = new MeshKit();
            k.Box(new Vector3(0.5f, 0, 0), Vector3.one, Color.white); // pivot on the left edge
            mat = UDFAssets.Glow_(c);
            var t = MeshKit.Spawn(name, k.Build(name), mat, m_BarRoot).transform;
            t.localPosition = pos - new Vector3(size.x * 0.5f, 0, 0);
            t.localScale = size;
            return t;
        }

        void ShowToast(string msg) { m_Toasts.Enqueue(msg); }

        void ShowCatchCard(FishAgent fish)
        {
            var sp = fish.Species;
            int count = collection.Data.Count(sp.id);
            string rc = ColorUtility.ToHtmlStringRGB(FishSpecies.RarityColor(sp.rarity));
            m_Card.richText = true;
            m_Card.text = $"{(count == 1 ? "<color=#FFD54A>NEW!</color>\n" : "")}<size=90>{sp.displayName}</size>\n{sp.englishName}\n" +
                          $"<color=#{rc}>【{FishSpecies.RarityName(sp.rarity)}】</color>  +{sp.coins} 金币\n<size=40>{sp.description}</size>";
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
            // lazy-follow hint panel
            Vector3 fwd = head.transform.forward.Flat();
            Vector3 target = head.transform.position + fwd * 1.1f + Vector3.down * 0.38f;
            m_HintRoot.position = Vector3.Lerp(m_HintRoot.position, target, Time.deltaTime * 3f);
            m_HintRoot.rotation = Quaternion.Slerp(m_HintRoot.rotation, Quaternion.LookRotation(m_HintRoot.position - head.transform.position), Time.deltaTime * 5f);

            string roomMsg = room.CurrentStatus == RoomUnderstanding.Status.Ready ? "" : room.StatusMessage;
            m_Hint.text = !string.IsNullOrEmpty(roomMsg) ? roomMsg : rod.Hint;

            // toasts
            m_ToastTimer -= Time.deltaTime;
            if (m_ToastTimer <= 0)
            {
                m_Toast.text = "";
                if (m_Toasts.Count > 0) { m_Toast.text = m_Toasts.Dequeue(); m_ToastTimer = 2.5f; }
            }

            // catch card pop
            if (m_CardTimer > 0)
            {
                m_CardTimer -= Time.deltaTime;
                float s = Mathf.SmoothStep(0, 1, Mathf.Clamp01((4f - m_CardTimer) * 5f)) * Mathf.Clamp01(m_CardTimer * 4f);
                m_CardRoot.localScale = Vector3.one * Mathf.Max(0.01f, s);
                if (m_CardTimer <= 0) m_CardRoot.gameObject.SetActive(false);
            }

            // fight bars
            bool fighting = rod.CurrentState == FishingRod.State.Fighting && rod.HookedFish != null;
            m_BarRoot.gameObject.SetActive(fighting);
            if (fighting)
            {
                m_BarRoot.position = rod.transform.position + Vector3.up * 0.16f;
                m_BarRoot.rotation = Quaternion.LookRotation(m_BarRoot.position - head.transform.position);
                float t = Mathf.Clamp01(rod.Tension);
                m_TensionFill.localScale = new Vector3(0.2f * Mathf.Max(0.01f, t), 0.014f, 0.002f);
                m_TensionMat.SetColor("_BaseColor", t > 0.85f ? new Color(1f, 0.3f, 0.3f) : t < 0.15f ? new Color(1f, 0.85f, 0.3f) : new Color(0.4f, 1f, 0.5f));
                var f = rod.HookedFish;
                m_StaminaFill.localScale = new Vector3(0.2f * Mathf.Max(0.01f, Mathf.Clamp01(f.Stamina / f.Species.stamina)), 0.007f, 0.002f);
            }

            // collection book
            if (XRInput.SecondaryDown(Hand.Left)) m_BookOpen = !m_BookOpen;
            if (XRInput.PrimaryDown(Hand.Left)) { room.Rescan(); ShowToast("重新扫描房间……"); }
            m_BookRoot.gameObject.SetActive(m_BookOpen);
            if (m_BookOpen) m_Book.text = BuildBookText();
        }

        string BuildBookText()
        {
            var d = collection.Data;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>图鉴</b>  {d.caught.Count}/{FishSpecies.All.Count}    金币 {d.coins}");
            sb.AppendLine($"鱼竿：{collection.CurrentRod.displayName}   (右手 B 切换)");
            sb.AppendLine();
            foreach (var sp in FishSpecies.All)
            {
                int n = d.Count(sp.id);
                string rc = ColorUtility.ToHtmlStringRGB(FishSpecies.RarityColor(sp.rarity));
                sb.AppendLine(n > 0 ? $"<color=#{rc}>●</color> {sp.displayName} ×{n}" : $"<color=#{rc}>○</color> ？？？");
            }
            sb.AppendLine();
            sb.AppendLine("<b>装饰品</b>");
            foreach (var dec in CollectionManager.Decor)
                sb.AppendLine(d.unlockedDecor.Contains(dec.id) ? $"√ {dec.displayName}" : $"[未解锁] {dec.requirement}");
            sb.AppendLine();
            sb.AppendLine("左手 X：重新扫描房间");
            return sb.ToString();
        }
    }
}
