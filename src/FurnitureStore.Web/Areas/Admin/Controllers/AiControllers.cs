using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace FurnitureStore.Web.Areas.Admin.Controllers;

/// <summary>/admin/ai - read-only review of the customers' AI conversations (quality, errors, token usage).</summary>
[Route("admin/ai")]
public sealed class AiConversationsController(IAiAdminService ai) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] AdminAiConversationQuery query, CancellationToken cancellationToken)
    {
        ViewData["Query"] = query;
        ViewData["AiEnabled"] = ai.AiEnabled;
        return View(await ai.ListConversationsAsync(query, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        View(await ai.GetConversationAsync(id, cancellationToken));
}

/// <summary>/admin/ai-knowledge - policies and FAQ the chatbot uses ("Quản lý nội dung chatbot").</summary>
[Route("admin/ai-knowledge")]
public sealed class AiKnowledgeController(IAiAdminService ai) : AdminControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await ai.ListKnowledgeAsync(cancellationToken));

    [HttpGet("create")]
    public IActionResult Create() => View("Form", new AiKnowledgeCommand());

    [HttpPost("create")]
    public Task<IActionResult> Create(AiKnowledgeCommand command, CancellationToken cancellationToken) => SaveAsync(null, command, cancellationToken);

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var entry = await ai.GetKnowledgeAsync(id, cancellationToken);
        ViewData["Id"] = id;
        return View("Form", new AiKnowledgeCommand
        {
            Title = entry.Title, Content = entry.Content, Category = entry.Category, Keywords = entry.Keywords,
            IsActive = entry.IsActive, DisplayOrder = entry.DisplayOrder
        });
    }

    [HttpPost("{id:int}/edit")]
    public Task<IActionResult> Edit(int id, AiKnowledgeCommand command, CancellationToken cancellationToken) => SaveAsync(id, command, cancellationToken);

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await ai.DeleteKnowledgeAsync(id, cancellationToken);
        SetStatus("Đã xóa nội dung chatbot.");
        return Redirect("/admin/ai-knowledge");
    }

    private async Task<IActionResult> SaveAsync(int? id, AiKnowledgeCommand command, CancellationToken cancellationToken)
    {
        try
        {
            ModelState.ThrowIfBindingFailed();
            await ai.SaveKnowledgeAsync(id, command, cancellationToken);
            SetStatus(id is null ? "Đã thêm nội dung chatbot." : "Đã cập nhật nội dung chatbot.");
            return Redirect("/admin/ai-knowledge");
        }
        catch (AppValidationException ex)
        {
            ModelState.AddApplicationErrors(ex, prefix: string.Empty);
            ViewData["Id"] = id;
            return View("Form", command);
        }
    }
}
