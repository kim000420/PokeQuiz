// Assets/Scripts/Managers/UIManager.cs

using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("UI Panels")]
    [Tooltip("로그인 UI 그룹 (Window Login)")]
    [SerializeField] private GameObject loginUIPanel;

    [Tooltip("게임 UI 그룹 (채팅창, 퀴즈 팝업 등)")]
    [SerializeField] private GameObject gameUIPanel;

    private void Start()
    {
        // 게임 시작 시: 로그인 UI만 켜고, 게임 UI는 끔
        ShowLoginUI();
    }

    private void OnEnable()
    {
        // NetworkManager의 로그인 결과 이벤트를 구독
        NetworkManager.OnLoginResultReceived += HandleLoginResult;
    }

    private void OnDisable()
    {
        NetworkManager.OnLoginResultReceived -= HandleLoginResult;
    }

    private void HandleLoginResult(SharedPackets.LoginResponsePacket pkt)
    {
        if (pkt.success)
        {
            // 로그인 성공 -> 게임 화면으로 전환
            ShowGameUI();
        }
        else
        {
            // 로그인 실패 -> (LoginUI가 알아서 에러 메시지를 띄울 것이므로 여기선 로그만)
            Debug.Log($"로그인 실패: {pkt.message}");
        }
    }

    public void ShowLoginUI()
    {
        if (loginUIPanel != null) loginUIPanel.SetActive(true);
        if (gameUIPanel != null) gameUIPanel.SetActive(false);
    }

    public void ShowGameUI()
    {
        if (loginUIPanel != null) loginUIPanel.SetActive(false);
        if (gameUIPanel != null) gameUIPanel.SetActive(true);
    }
}