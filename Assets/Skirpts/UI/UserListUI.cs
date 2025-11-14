using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// [옵저버] 접속자 명단과 점수를 표시하는 UI (Scroll View)
/// </summary>
public class UserListUI : MonoBehaviour
{
    [Header("접속자 수 (2/6)")]
    [Tooltip("접속자 수를 표시할 텍스트 (예: 2/6)")]
    [SerializeField] private TMP_Text userCountText;

    [Header("유저 슬롯 목록")]
    [Tooltip("유저 정보가 표시될 6개의 Text (TMP) 슬롯 리스트")]
    [SerializeField] private List<TMP_Text> userListSlots = new List<TMP_Text>();

    private void OnEnable()
    {
        // 2개의 분리된 이벤트 구독
        NetworkManager.OnUserCountUpdated += UpdateUserCount;
        NetworkManager.OnUserListReceived += UpdateUserList;
    }

    private void OnDisable()
    {
        NetworkManager.OnUserCountUpdated -= UpdateUserCount;
        NetworkManager.OnUserListReceived -= UpdateUserList;
    }

    /// <summary>
    /// (요구사항 1) 접속자 수 텍스트 갱신 (예: "2/6")
    /// </summary>
    private void UpdateUserCount(string countText)
    {
        if (userCountText != null)
        {
            userCountText.text = countText;
        }
    }

    /// <summary>
    /// (요구사항 2) 6개의 슬롯에 유저 목록 갱신 (예: "유저1 [2/0]")
    /// </summary>
    private void UpdateUserList(List<UserData> users)
    {
        // 6개의 슬롯을 순회
        for (int i = 0; i < userListSlots.Count; i++)
        {
            if (userListSlots[i] == null) continue; // 슬롯이 비었으면 건너뛰기

            // 이 슬롯(i)에 해당하는 유저가 '있는지' 확인
            if (i < users.Count)
            {
                // [데이터 있음] 텍스트 채우기 
                UserData user = users[i];
                userListSlots[i].text = $"{user.Nickname} [{user.Score}]";
                userListSlots[i].gameObject.SetActive(true); // 슬롯 활성화
            }
            else
            {
                // [데이터 없음] 빈 슬롯 처리
                userListSlots[i].text = ""; // 텍스트 비우기
                userListSlots[i].gameObject.SetActive(false); // 슬롯 비활성화
            }
        }
    }
}