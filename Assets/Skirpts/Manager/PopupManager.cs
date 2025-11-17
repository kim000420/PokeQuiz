// Assets/Scripts/Managers/PopupManager.cs

using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using SharedPackets;
using DG.Tweening;

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
    
    [Header("Settings")]
    [Tooltip("정답 팝업이 떠있는 시간 (초)")]
    [SerializeField] private float winnerDisplayDuration = 3.0f;
    [Tooltip("팝업 등장 애니메이션 시간 (초)")]
    [SerializeField] private float animationDuration = 0.5f;
    [Tooltip("힌트 텍스트 등장 시간 (초)")]
    [SerializeField] private float hintAnimDuration = 0.3f;
    [Tooltip("힌트 등장 이징 효과")]
    [SerializeField] private Ease hintAnimEase = Ease.OutBack;

    private int _currentHintIndex = 0;
    private Coroutine _winnerPopupRoutine;

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
        StopWinnerRoutine();
        InitializeHintPopup();
    }

    private void HandleHintReceived(HintPacket pkt)
    {
        ShowNextHint(pkt.hintContent);
    }

    private void HandleWinnerReceived(WinnerPacket pkt)
    {
        if (_winnerPopupRoutine != null) StopCoroutine(_winnerPopupRoutine);
        _winnerPopupRoutine = StartCoroutine(ShowWinnerPopupRoutine(pkt.winnerName, pkt.answerPokemon));
    }

    private void HandleQuizEnded(QuizEndPacket pkt)
    {
        if (hintPopupObject != null) hintPopupObject.SetActive(false);
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

            // 힌트 인덱스를 0으로 리셋
            _currentHintIndex = 0;

            // 5개의 텍스트 슬롯을 모두 '???' (또는 빈 문자열 "")로 초기화
            foreach (var slot in hintTextSlots)
            {
                slot.text = "???";
                // 애니메이션 후 크기가 0이거나 변형되었을 수 있으므로 정사이즈(1)로 리셋
                slot.transform.localScale = Vector3.one;
            }
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
            TMP_Text targetSlot = hintTextSlots[_currentHintIndex];

            // 텍스트 내용 변경 ("???" -> "실제 힌트")
            targetSlot.text = content;

            // 기존에 "???"가 있던 자리에서, 새 텍스트가 0부터 커지며 "톡!" 튀어나오는 효과
            targetSlot.transform.DOKill(); // 혹시 모를 이전 트윈 제거
            targetSlot.transform.localScale = Vector3.zero; // 크기 0으로 초기화
            targetSlot.transform.DOScale(1f, hintAnimDuration).SetEase(hintAnimEase);

            _currentHintIndex++;
        }
    }
    /// <summary>
    /// 애니메이션과 대기 시간을 처리하는 코루틴
    /// </summary>
    private IEnumerator ShowWinnerPopupRoutine(string name, string pokemon)
    {
        // 힌트 팝업 끄기
        if (hintPopupObject != null) hintPopupObject.SetActive(false);

        // 데이터 설정
        if (winnerPopupObject != null)
        {
            winnerNameText.text = name;
            winnerAnswerText.text = pokemon;

            winnerPopupObject.transform.DOKill();

            // 초기화 (크기를 0으로)
            winnerPopupObject.transform.localScale = Vector3.zero;
            winnerPopupObject.SetActive(true);

            // 등장 애니메이션 (Scale 0 -> 1, 부드럽게)
            winnerPopupObject.transform.DOScale(1f, animationDuration).SetEase(Ease.OutBack);

            // 유지 시간 대기 (n초)
            yield return new WaitForSeconds(winnerDisplayDuration);

            winnerPopupObject.SetActive(false);
        }

        _winnerPopupRoutine = null;
    }

    /// <summary>
    /// 정답자 팝업활성화 및 텍스트 채우기
    /// </summary>
    private void StopWinnerRoutine()
    {
        if (_winnerPopupRoutine != null)
        {
            StopCoroutine(_winnerPopupRoutine);
            _winnerPopupRoutine = null;
        }

        if (winnerPopupObject != null)
        {
            // [DOTween] 트윈도 즉시 멈춤
            winnerPopupObject.transform.DOKill();
            winnerPopupObject.SetActive(false);
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