// /home/rsa-key-20251109/projects/PokemonChatServer/Pokemon.cs

/// <summary>
/// MySQL DB의 'Pokemons' 테이블 구조와 1:1로 매핑되는 C# 클래스입니다.
/// </summary>
public class Pokemon
{
    // C#의 속성(Property) 이름은 DB의 컬럼(Column) 이름과
    // 대소문자까지 정확히 일치해야 MySqlConnector가 자동으로 매핑해 줍니다.

    // (참고: API 서버에서 썼던 [Key], [Ignore] 같은 태그가 필요 없습니다.
    // MySqlConnector는 이름 기반으로 데이터를 '직접' 읽어옵니다.)

    public int Id { get; set; }
    public int DexId { get; set; }
    public string SpeciesEngName { get; set; } = "";
    public string SpeciesKorName { get; set; } = "";
    public int FormId { get; set; }
    public string FormEngName { get; set; } = "";
    public string FormKey { get; set; } = "";
    public string TypeA { get; set; } = "";
    public string? TypeB { get; set; } // 'NULL'일 수 있으므로 '?' (nullable)
    public int Generation { get; set; }
    public bool GenderUnknown { get; set; }
    public float GenderMale { get; set; }
    public float GenderFemale { get; set; }
    public int EggSteps { get; set; }
    public string EggGroup1 { get; set; } = "";
    public string? EggGroup2 { get; set; } // 'NULL'일 수 있으므로 '?' (nullable)
    public int CatchRate { get; set; }
    public string ExperienceGroup { get; set; } = "";
    public string RarityCategory { get; set; } = "";
    public int H { get; set; }
    public int A { get; set; }
    public int B { get; set; }
    public int C { get; set; }
    public int D { get; set; }
    public int S { get; set; }
    public int Total { get; set; }
}