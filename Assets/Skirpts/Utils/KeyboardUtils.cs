// Assets/Scripts/Utils/KeyboardUtils.cs

using UnityEngine;

public static class KeyboardUtils
{
    /// <summary>
    /// 현재 플랫폼에 맞는 키보드 높이를 반환합니다. (픽셀 단위)
    /// </summary>
    public static float GetKeyboardHeight()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // 안드로이드 네이티브 코드 호출
        using (AndroidJavaClass unityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject unityPlayer = unityClass.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject view = unityPlayer.Call<AndroidJavaObject>("getWindow").Call<AndroidJavaObject>("getDecorView");
            
            if (view == null) return 0;

            // 화면 전체 높이 구하기
            int screenHeight = new AndroidJavaClass("android.content.res.Resources").GetStatic<AndroidJavaObject>("getSystem").Call<AndroidJavaObject>("getDisplayMetrics").Get<int>("heightPixels");
            
            // 현재 보이는 영역(키보드 제외) 구하기
            AndroidJavaObject rect = new AndroidJavaObject("android.graphics.Rect");
            view.Call("getWindowVisibleDisplayFrame", rect);
            int visibleHeight = rect.Call<int>("height");

            // 차이만큼이 키보드 높이
            int keyboardHeight = screenHeight - visibleHeight;
            
            // 오차 보정 (네비게이션 바 등)
            return keyboardHeight > 100 ? keyboardHeight : 0;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        // iOS는 TouchScreenKeyboard.area가 비교적 정확함 (단, 인스턴스가 필요할 수 있음)
        return (float)TouchScreenKeyboard.area.height;
#else
        // 에디터 테스트용 가짜 높이 (600px)
        return 1500f;
#endif
    }
}