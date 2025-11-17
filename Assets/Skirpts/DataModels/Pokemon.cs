// Assets/Scripts/DataModels/Pokemon.cs 
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

    [JsonProperty("typeB")]
    public string? TypeB { get; set; }

    [JsonProperty("generation")]
    public int Generation { get; set; }

    [JsonProperty("genderUnknown")]
    public bool GenderUnknown { get; set; }

    [JsonProperty("genderMale")]
    public float GenderMale { get; set; }

    [JsonProperty("genderFemale")]
    public float GenderFemale { get; set; }

    [JsonProperty("eggSteps")]
    public int EggSteps { get; set; }

    [JsonProperty("eggGroup1")]
    public string EggGroup1 { get; set; }

    [JsonProperty("eggGroup2")]
    public string? EggGroup2 { get; set; }

    [JsonProperty("catchRate")]
    public int CatchRate { get; set; }

    [JsonProperty("experienceGroup")]
    public string ExperienceGroup { get; set; }

    [JsonProperty("rarityCategory")]
    public string RarityCategory { get; set; }

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