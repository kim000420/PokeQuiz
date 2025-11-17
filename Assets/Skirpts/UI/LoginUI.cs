// Assets/Scripts/UI/LoginUI.cs

using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class LoginUI : MonoBehaviour
{
    [Header("UI Components")]
    [Tooltip("닉네임 입력 필드")]
    [SerializeField] private TMP_InputField nicknameInputField;

    [Tooltip("Join 버튼")]
    [SerializeField] private Button joinButton;

    [Tooltip("Info 그룹 (에러 메시지 표시용)")]
    [SerializeField] private GameObject infoGroup;

    [Tooltip("Info 그룹 내부의 텍스트")]
    [SerializeField] private TMP_Text infoText;

    private void Start()
    {
        // 시작 시 Info 창은 숨김
        if (infoGroup != null) infoGroup.SetActive(false);

        // 닉네임 최대 길이 설정 (6글자)
        if (nicknameInputField != null)
        {
            nicknameInputField.characterLimit = 6;
        }
    }

    private void OnEnable()
    {
        joinButton.onClick.AddListener(TryLogin);
        NetworkManager.OnLoginResultReceived += OnLoginResult;
    }

    private void OnDisable()
    {
        joinButton.onClick.RemoveListener(TryLogin);
        NetworkManager.OnLoginResultReceived -= OnLoginResult;
    }

    /// <summary>
    /// Join 버튼 클릭 시 호출
    /// </summary>
    private void TryLogin()
    {
        string nickname = nicknameInputField.text.Trim();

        // 유효성 검사 (클라이언트 측)
        if (string.IsNullOrEmpty(nickname))
        {
            ShowError("닉네임을 입력해주세요.");
            return;
        }

        if (nickname.Length < 1) // (혹시 모를 최소 길이)
        {
            ShowError("닉네임이 너무 짧습니다.");
            return;
        }

        // (한글 6자 제한은 InputField 설정으로 1차 막고, 서버에서도 2차 검증함)

        // 서버 접속 및 로그인 요청
        // 버튼 중복 클릭 방지
        joinButton.interactable = false;

        // NetworkManager에게 요청 (비동기)
        _ = NetworkManager.Instance.ConnectAndLoginAsync(nickname);
    }

    /// <summary>
    /// 서버로부터 응답이 왔을 때 처리
    /// </summary>
    private void OnLoginResult(SharedPackets.LoginResponsePacket pkt)
    {
        joinButton.interactable = true; // 버튼 다시 활성화

        if (!pkt.success)
        {
            // 유효성 검사 실패 (또는 DB 오류) -> Info UI 활성화
            ShowError(pkt.message);
        }
        else
        {
            // 성공 -> (UIManager가 화면을 전환할 것이므로 LoginUI는 할 일 없음)
            // 필요하다면 입력창 초기화 정도
            nicknameInputField.text = "";
            if (infoGroup != null) infoGroup.SetActive(false);
        }
    }

    private void ShowError(string message)
    {
        if (infoGroup != null)
        {
            infoGroup.SetActive(true);
            if (infoText != null) infoText.text = message;
        }
    }
}