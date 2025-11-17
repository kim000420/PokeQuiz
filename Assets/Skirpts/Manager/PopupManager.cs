// Assets/Scripts/Managers/PopupManager.cs

using UnityEngine;
using TMPro;
using System.Collections.Generic;
using SharedPackets;

/// <summary>
/// Quiz-Chat UI 영역의 모든 팝업을 관리합니다.
/// NetworkManager의 이벤트를 '구독'하여 힌트 팝업과 정답자 팝업을 제어합니다.
/// </summary>
public class PopupManager : MonoBehaviour
{
    private static PopupManager _instance;
    public static PopupManager Instance => _instance;

    [Header("Popup Hint (힌트 팝업)")]
    [Tooltip("Popup Hint 그룹의 부모 GameObject")]
    [SerializeField] private GameObject hintPopupObject;
    [Tooltip("힌트 1~5번이 표시될 Text (TMP) 슬롯 5개를 순서대로 연결")]
    [SerializeField] private List<TMP_Text> hintTextSlots;

    [Header("Popup Winner (정답자 팝업)")]
    [Tooltip("Popup Winner 그룹의 부모 GameObject")]
    [SerializeField] private GameObject winnerPopupObject;
    [Tooltip("정답자 이름이 표시될 TMP_Text (TMP-Text_Winner)")]
    [SerializeField] private TMP_Text winnerNameText;
    [Tooltip("정답 포켓몬 이름이 표시될 TMP_Text (TMP-Text_Answer)")]
    [SerializeField] private TMP_Text winnerAnswerText;

    private int _currentHintIndex = 0;

    private void Awake()
    {
        if (_instance == null) _instance = this;
    }

    private void Start()
    {
        // 시작 시 모든 팝업을 비활성화(숨기기)
        if (hintPopupObject != null) hintPopupObject.SetActive(false);
        if (winnerPopupObject != null) winnerPopupObject.SetActive(false);
    }

    private void OnEnable()
    {
        NetworkManager.OnQuizStarted += HandleQuizStarted;
        NetworkManager.OnHintReceived += HandleHintReceived;
        NetworkManager.OnWinnerReceived += HandleWinnerReceived;
        NetworkManager.OnQuizEnded += HandleQuizEnded;
    }

    private void OnDisable()
    {
        NetworkManager.OnQuizStarted -= HandleQuizStarted;
        NetworkManager.OnHintReceived -= HandleHintReceived;
        NetworkManager.OnWinnerReceived -= HandleWinnerReceived;
        NetworkManager.OnQuizEnded -= HandleQuizEnded;
    }

    /// <summary>
    /// NetworkManager로부터 '메시지 수신' 신호를 받았을 때 호출됩니다.
    /// </summary>
    private void HandleQuizStarted(QuizStartPacket pkt)
    {
        InitializeHintPopup();
    }

    private void HandleHintReceived(HintPacket pkt)
    {
        ShowNextHint(pkt.hintContent);
    }

    private void HandleWinnerReceived(WinnerPacket pkt)
    {
        ShowWinnerPopup(pkt.winnerName, pkt.answerPokemon);
    }

    private void HandleQuizEnded(QuizEndPacket pkt)
    {
        HideAllPopups();
    }

    /// <summary>
    /// 힌트 팝업(Popup Hint)을 활성화하고 5개의 슬롯을 '???'로 초기화합니다.
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
            foreach (var slot in hintTextSlots) slot.text = "???";
        }
    }

    /// <summary>
    /// 다음 힌트 슬롯에 텍스트 채우기
    /// </summary>
    private void ShowNextHint(string content)
    {
        if (hintPopupObject != null) hintPopupObject.SetActive(true);

        if (_currentHintIndex < hintTextSlots.Count)
        {
            hintTextSlots[_currentHintIndex].text = content;
            _currentHintIndex++;
        }
    }

    /// <summary>
    /// 정답자 팝업활성화 및 텍스트 채우기
    /// </summary>
    private void ShowWinnerPopup(string name, string pokemon)
    {
        if (hintPopupObject != null) hintPopupObject.SetActive(false);

        if (winnerPopupObject != null)
        {
            winnerPopupObject.SetActive(true);
            winnerNameText.text = name;
            winnerAnswerText.text = pokemon;
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