// Assets/Scripts/UI/SendButtonTrigger.cs

using UnityEngine;
using UnityEngine.EventSystems; // [필수] 인터페이스 사용

public class SendButtonTrigger : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private ChatWindowController windowController;

    // 버튼이 눌리는 순간(Down) 호출됩니다. (OnClick보다 빠름)
    public void OnPointerDown(PointerEventData eventData)
    {
        if (windowController != null)
        {
            // "지금 전송 버튼 눌렀으니까, 곧 발생할 OnEndEdit은 무시해!"
            windowController.BlockNextExpand = true;
        }
    }
}