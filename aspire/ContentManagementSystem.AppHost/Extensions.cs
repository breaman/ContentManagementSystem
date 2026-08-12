using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.AppHost;

public static class Extensions
{
    extension<T>(IResourceBuilder<T> builder) where T : IResource
    {
        public IResourceBuilder<T> InitiallyHidden()
        {
            builder.WithInitialState(new CustomResourceSnapshot()
            {
                ResourceType = builder.Resource.GetType().Name,
                Properties = [],
                IsHidden = true
            });
            return builder;
        }

        public IResourceBuilder<T> HideWhen(string state)
        {
            builder.OnBeforeResourceStarted((resource, evt, ct) =>
            {
                var rns = evt.Services.GetRequiredService<ResourceNotificationService>();
                _ = Task.Run(async () =>
                {
                    await rns.WaitForResourceAsync(resource.Name, state, ct);
                    await rns.PublishUpdateAsync(resource, x => x with
                    {
                        IsHidden = true
                    });
                }, ct);
                return Task.CompletedTask;
            });
            return builder;
        }
    }
}