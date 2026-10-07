using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Engagement;

namespace FurnitureStore.Web.ViewModels;

public sealed record HomeViewModel(HomePageDto Page, StoreInfoDto Store, IReadOnlyList<HomeReviewDto> Reviews, HomeBannerDto Banner);
