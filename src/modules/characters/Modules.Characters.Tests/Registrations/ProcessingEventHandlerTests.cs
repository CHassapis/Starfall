using Microsoft.Extensions.Logging;
using Moq;
using Starlights.Modules.Characters.Data;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Characters.Domain.Registrations;
using Starlights.Modules.Characters.Domain.Registrations.Eventing;
using Starlights.Modules.Characters.Services.EventHandlers;
using Starlights.Modules.Characters.Services.Processing;
using Starlights.Modules.Elements.Integration;
using Starlights.Platform.Data;

namespace Starlights.Modules.Characters.Tests.Registrations;

[TestClass]
public sealed class ProcessingEventHandlerTests
{
    private readonly Mock<ILogger<ProcessingEventHandler>> _logger = new();
    private readonly Mock<IRegistrationProcessor> _processor = new();
    private readonly Mock<IPersistence> _persistence = new();
    private readonly Mock<IRegistrationRepository> _registrations = new();
    private readonly Mock<IElementsModuleQueries> _elements = new();

    private ProcessingEventHandler CreateHandler()
    {
        _persistence.Setup(p => p.GetRepository<IRegistrationRepository>()).Returns(_registrations.Object);
        return new ProcessingEventHandler(_logger.Object, _processor.Object, _persistence.Object, _elements.Object);
    }

    private static RegistrationCreatedEvent Created(Guid registrationId) => new()
    {
        CharacterId = Guid.NewGuid(),
        RegistrationId = registrationId,
        AssociatedElementName = "Skill Proficiency",
        AssociatedElementType = "Proficiency",
    };

    private void VerifyLogged(LogLevel level, Times times) =>
        _logger.Verify(l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    [TestMethod]
    public async Task HandleAsync_SkipsWithoutAnError_WhenTheRegistrationWasRemovedBeforeItsEvent()
    {
        var registrationId = Guid.NewGuid();
        _registrations.Setup(r => r.GetRegistrationAsync(new RegistrationId(registrationId))).ReturnsAsync((Registration?)null);

        await CreateHandler().HandleAsync(Created(registrationId));

        _processor.Verify(p => p.ProcessRegistration(It.IsAny<RegistrationId>()), Times.Never);
        VerifyLogged(LogLevel.Error, Times.Never());
        VerifyLogged(LogLevel.Information, Times.Once());
    }

    [TestMethod]
    public async Task HandleAsync_StillLogsAnError_WhenProcessingFails()
    {
        var registration = Registration.Create(CharacterId.New(), new(Guid.NewGuid()), "Base", "Type");
        var registrationId = registration.Id.Value;
        _registrations.Setup(r => r.GetRegistrationAsync(registration.Id)).ReturnsAsync(registration);
        _processor.Setup(p => p.ProcessRegistration(registration.Id))
                  .ThrowsAsync(new RegistrationProcessingException("the associated element was not found"));

        await CreateHandler().HandleAsync(Created(registrationId));

        VerifyLogged(LogLevel.Error, Times.Once());
    }
}
