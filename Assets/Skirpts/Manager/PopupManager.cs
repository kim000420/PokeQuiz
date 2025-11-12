// Assets/Scripts/Managers/PopupManager.cs

using UnityEngine;
using TMPro; // TextMeshPro (TMP) UI를 사용하기 위해
using System; // Action 이벤트를 위해
using System.Collections.Generic;

/// <summary>
/// [싱글톤 옵저버] '3. Quiz-Chat UI' 영역의 모든 팝업을 관리합니다.
/// NetworkManager의 이벤트를 '구독'하여 힌트 팝업과 정답자 팝업을 제어합니다.
/// </summary>
public class PopupManager : MonoBehaviour
{
    // --- 1. 싱글톤 설정 ---
    private static PopupManager _instance;
    public static PopupManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<PopupManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("PopupManager");
                    _instance = go.AddComponent<PopupManager>();
                }
            }
            return _instance;
        }
    }

    [Header("Popup Hint (힌트 팝업)")]
    [Tooltip("Popup Hint 그룹의 부모 GameObject")]
    [SerializeField] private GameObject hintPopupObject; 
    [Tooltip("힌트 1~5번이 표시될 Text (TMP) 슬롯 5개를 순서대로 연결")]
    [SerializeField] private List<TMP_Text> hintTextSlots = new List<TMP_Text>();

    [Header("Popup Winner (정답자 팝업)")]
    [Tooltip("Popup Winner 그룹의 부모 GameObject")]
    [SerializeField] private GameObject winnerPopupObject;
    [Tooltip("정답자 이름이 표시될 TMP_Text (TMP-Text_Winner)")]
    [SerializeField] private TMP_Text winnerNameText;
    [Tooltip("정답 포켓몬 이름이 표시될 TMP_Text (TMP-Text_Answer)")]
    [SerializeField] private TMP_Text winnerAnswerText;

    private int _currentHintIndex = 0;
    // --- 2. Unity 생명주기 및 옵저버 구독 ---

    private void Awake()
    {
        // 싱글톤 인스턴스 관리
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    private void Start()
    {
        // 시작 시 모든 팝업을 비활성화(숨기기)
        if (hintPopupObject != null) hintPopupObject.SetActive(false);
        if (winnerPopupObject != null) winnerPopupObject.SetActive(false);
    }

    private void OnEnable()
    {
        // NetworkManager의 '메시지 수신' 이벤트를 '구독'
        NetworkManager.OnMessageReceived += HandleServerMessage;
    }

    private void OnDisable()
    {
        // '구독 해제' (메모리 누수 방지)
        NetworkManager.OnMessageReceived -= HandleServerMessage;
    }

    // --- 3. 이벤트 핸들러 (신호 수신) ---

    /// <summary>
    /// NetworkManager로부터 '메시지 수신' 신호를 받았을 때 호출됩니다.
    /// </summary>
    private void HandleServerMessage(string message)
    {
        // 퀴즈가 '시작'될 때 (힌트 팝업 초기화)
        if (message.StartsWith("[퀴즈] 새 퀴즈를") || message.StartsWith("[퀴즈] 문제를 가져왔습니다"))
        {
            InitializeHintPopup(); // 5개 슬롯 초기화
        }
        // 힌트가 '추가'될 때 (다음 슬롯 채우기)
        else if (message.StartsWith("[힌트]"))
        {
            ShowNextHint(message); // 다음 힌트 슬롯에 텍스트 할당
        }
        // [정답!] 메시지 감지
        else if (message.StartsWith("[정답!]"))
        {
            ShowWinnerPopup(message);
        }
        // [시간 초과] 또는 [퀴즈 종료] 메시지 감지
        else if (message.StartsWith("[시간 초과]") || message.StartsWith("[퀴즈] 퀴즈가 종료되었습니다"))
        {
            HideAllPopups();
        }
    }

    // --- 4. 팝업 제어 함수 ---

    /// <summary>
    /// [새 함수] 힌트 팝업(Popup Hint)을 활성화하고 5개의 슬롯을 '???'로 초기화합니다.
    /// </summary>
    private void InitializeHintPopup()
    {
        if (winnerPopupObject != null) winnerPopupObject.SetActive(false); // 정답자 팝업 숨김
        if (hintPopupObject != null)
        {
            hintPopupObject.SetActive(true);

            // [핵심] 힌트 인덱스를 0으로 리셋
            _currentHintIndex = 0;

            // 5개의 텍스트 슬롯을 모두 '???' (또는 빈 문자열 "")로 초기화
            foreach (TMP_Text slot in hintTextSlots)
            {
                if (slot != null)
                {
                    slot.text = "???"; // 님이 원하는 초기 텍스트
                }
            }
        }
    }

    /// <summary>
    /// [새 함수] 다음 힌트 슬롯에 텍스트를 채웁니다.
    /// </summary>
    private void ShowNextHint(string hintMessage)
    {
        // 퀴즈 시작 시 팝업이 활성화되었어야 하지만, 안전을 위해 체크
        if (hintPopupObject != null && !hintPopupObject.activeSelf)
        {
            hintPopupObject.SetActive(true);
        }

        // [핵심] 힌트 인덱스가 5개를 넘어가지 않았는지 확인
        if (_currentHintIndex < hintTextSlots.Count)
        {
            // 1. 현재 인덱스에 해당하는 텍스트 슬롯(slot)을 가져옴
            TMP_Text currentSlot = hintTextSlots[_currentHintIndex];

            if (currentSlot != null)
            {
                // 2. 힌트 메시지에서 태그 제거
                string cleanMessage = hintMessage.Substring(hintMessage.IndexOf("]") + 1).Trim();

                // 3. 해당 슬롯의 텍스트를 갱신
                currentSlot.text = cleanMessage;
            }

            // 4. 다음 힌트를 받을 수 있도록 인덱스 1 증가
            _currentHintIndex++;
        }
        else
        {
            // (방어 코드) 5개가 꽉 찼는데 힌트가 또 들어온 경우 (예: 서버 로직 변경)
            Debug.LogWarning("[PopupManager] 힌트 슬롯 5개가 모두 찼습니다.");
        }
    }

    /// <summary>
    /// 정답자 팝업(Popup Winner)을 활성화하고 텍스트를 파싱하여 설정합니다.
    /// </summary>
    private void ShowWinnerPopup(string winnerMessage)
    {
        if (hintPopupObject != null) hintPopupObject.SetActive(false); // 힌트 팝업은 숨김
        if (winnerPopupObject != null)
        {
            winnerPopupObject.SetActive(true);

            // [핵심] 서버 메시지 파싱(Parsing)
            // 예: "[정답!] '유니티테스터' 님이 정답 '꼬이밍고'을(를) 맞혔습니다!"
            try
            {
                // '...' 로 닉네임과 정답 추출
                string[] parts = winnerMessage.Split('\'');
                string winnerName = parts[1]; // "유니티테스터"
                string answerName = parts[3]; // "꼬이밍고"

                if (winnerNameText != null) winnerNameText.text = winnerName;
                if (winnerAnswerText != null) winnerAnswerText.text = answerName;
            }
            catch (Exception e)
            {
                // 파싱 실패 시 원본 메시지 표시 (방어 코드)
                Debug.LogError($"[PopupManager] 정답 메시지 파싱 실패: {e.Message}");
                if (winnerNameText != null) winnerNameText.text = "Error";
                if (winnerAnswerText != null) winnerAnswerText.text = winnerMessage;
            }
        }
    }

    /// <summary>
    /// 모든 퀴즈 팝업을 비활성화(숨기기)합니다.
    /// </summary>
    private void HideAllPopups()
    {
        if (hintPopupObject != null) hintPopupObject.SetActive(false);
        if (winnerPopupObject != null) winnerPopupObject.SetActive(false);

        // 힌트 인덱스 리셋
        _currentHintIndex = 0;
    }
}