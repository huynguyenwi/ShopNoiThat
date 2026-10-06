namespace FurnitureStore.Domain.Enums;

/// <summary>Publishing state of a product.</summary>
public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Inactive = 2,
    Discontinued = 3
}

/// <summary>
/// Kind of furniture. Used for "shop by type" navigation, AI recommendations and custom price calculation.
/// </summary>
public enum FurnitureType
{
    Table = 1,
    Chair = 2,
    Sofa = 3,
    Bed = 4,
    Cabinet = 5,
    Shelf = 6,
    Decor = 7,
    Other = 99
}

/// <summary>Family a material belongs to.</summary>
public enum MaterialGroup
{
    NaturalWood = 1,
    EngineeredWood = 2,
    Fabric = 3,
    Leather = 4,
    Metal = 5,
    Stone = 6,
    Glass = 7,
    Rattan = 8,
    Other = 99
}

/// <summary>Surface finish (paint / coating) used for custom furniture quotes.</summary>
public enum FinishType
{
    NaturalOil = 1,
    PU = 2,
    NC = 3,
    TwoK = 4,
    Lacquer = 5
}
