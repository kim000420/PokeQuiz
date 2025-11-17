// Assets/Scripts/UI/UserInfoUI.cs

using UnityEngine;
using TMPro;

public class UserInfoUI : MonoBehaviour
{
    [Header("UI Components")]
    [Tooltip("닉네임을 표시할 TMP_Text 컴포넌트")]
    [SerializeField] private TMP_Text nicknameText;

    /// <summary>
    /// 이 UI가 활성화될 때(로그인 성공 후 UIManager가 켜줄 때) 자동으로 호출됩니다.
    /// </summary>
    private void OnEnable()
    {
        UpdateNickname();
    }

    private void UpdateNickname()
    {
        if (NetworkManager.Instance != null && nicknameText != null)
        {
            // NetworkManager에 저장된 내 닉네임을 가져와서 출력
            nicknameText.text = NetworkManager.Instance.MyNickname;
        }
    }
}