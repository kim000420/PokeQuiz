// Assets/Scripts/DataModels/Pokemon.cs (새 폴더 'DataModels'를 추천합니다)

// 1. JSON 변환기가 이 클래스를 사용하도록 선언
using Newtonsoft.Json;

/// <summary>
/// 서버 API로부터 받은 JSON 데이터를 파싱하기 위한 Unity용 포켓몬 모델입니다.
/// </summary>
[System.Serializable] // Unity 인스펙터에서 보기 위해 추가 (선택)
public class Pokemon
{
    // [JsonProperty("jsonKey")]는 JSON의 'camelCase' 키와
    // C#의 'PascalCase' 변수명을 매핑해 줍니다.

    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("dexId")]
    public int DexId { get; set; }

    [JsonProperty("speciesEngName")]
    public string SpeciesEngName { get; set; }

    [JsonProperty("speciesKorName")]
    public string SpeciesKorName { get; set; }

    [JsonProperty("formId")]
    public int FormId { get; set; }

    [JsonProperty("formEngName")]
    public string FormEngName { get; set; }

    [JsonProperty("typeA")]
    public string TypeA { get; set; }

    // JSON에서 'null'일 수 있는 값은 C#에서도 '?' (nullable)로 받아야 합니다.
    [JsonProperty("typeB")]
    public string? TypeB { get; set; }

    [JsonProperty("generation")]
    public int Generation { get; set; }

    [JsonProperty("h")]
    public int H { get; set; }

    [JsonProperty("a")]
    public int A { get; set; }

    [JsonProperty("b")]
    public int B { get; set; }

    [JsonProperty("c")]
    public int C { get; set; }

    [JsonProperty("d")]
    public int D { get; set; }

    [JsonProperty("s")]
    public int S { get; set; }

    [JsonProperty("total")]
    public int Total { get; set; }

    // TODO: 퀴즈에 필요한 다른 속성들(rarity, egg_group 등)도 여기에 추가하세요.
}