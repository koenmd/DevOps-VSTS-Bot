﻿// ———————————————————————————————
// <copyright file="ReleasesDialog.cs">
// Licensed under the MIT License. See License.txt in the project root for license information.
// </copyright>
// <summary>
// Represents the dialog to list/create releases.
// </summary>
// ———————————————————————————————
namespace Vsar.TSBot.Dialogs
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Cards;
    using Microsoft.Bot.Builder.Dialogs;
    using Microsoft.Bot.Connector;
    using Resources;

    /// <summary>
    /// Represents the dialog to list/create releases.
    /// </summary>
    [CommandMetadata("releases")]
    [Serializable]
    public class ReleasesDialog : DialogBase, IDialog<object>
    {
        private const int TakeSize = 7;

        private const string CommandMatchReleases = "releases";
        private const string CommandMatchCreate = @"create (\d+)";

        /// <summary>
        /// Initializes a new instance of the <see cref="ReleasesDialog"/> class.
        /// </summary>
        /// <param name="authenticationService">The authenticationService.</param>
        /// <param name="vstsService">The <see cref="IVstsService"/>.</param>
        public ReleasesDialog(IAuthenticationService authenticationService, IVstsService vstsService)
            : base(authenticationService, vstsService)
        {
        }

        /// <summary>
        /// Gets or sets the account.
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// Gets or sets the profile.
        /// </summary>
        public Profile Profile { get; set; }

        /// <summary>
        /// Gets or sets the Team Project.
        /// </summary>
        public string TeamProject { get; set; }

        /// <inheritdoc />
        public async Task StartAsync(IDialogContext context)
        {
            context.ThrowIfNull(nameof(context));

            context.Wait(this.ReleasesAsync);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets a list of release definitions.
        /// </summary>
        /// <param name="context">A <see cref="IDialogContext"/>.</param>
        /// <param name="result">A <see cref="IMessageActivity"/>/</param>
        /// <returns>A <see cref="Task"/>.</returns>
        public async Task ReleasesAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            context.ThrowIfNull(nameof(context));
            result.ThrowIfNull(nameof(result));

            var activity = await result;

            var data = context.UserData.GetValue<UserData>("userData");

            this.Account = data.Account;
            this.Profile = await this.GetValidatedProfile(context.UserData);
            this.TeamProject = data.TeamProject;

            var text = (activity.Text ?? string.Empty).Trim().ToLowerInvariant();

            if (text.Equals(CommandMatchReleases, StringComparison.OrdinalIgnoreCase))
            {
                var typing = context.MakeMessage();
                typing.Type = ActivityTypes.Typing;
                await context.PostAsync(typing);

                var releaseDefinitions =
                    await this.VstsService.GetReleaseDefinitionsAsync(this.Account, this.TeamProject, this.Profile.Token);
                if (!releaseDefinitions.Any())
                {
                    var reply = context.MakeMessage();
                    reply.Text = Labels.NoReleases;
                    await context.PostAsync(reply);
                    context.Done(reply);
                    return;
                }

                var skip = 0;
                while (skip < releaseDefinitions.Count)
                {
                    var cards = releaseDefinitions.Skip(skip).Take(TakeSize).Select(rd => new ReleaseDefinitionCard(rd)).ToList();
                    var reply = context.MakeMessage();

                    foreach (var card in cards)
                    {
                        reply.Attachments.Add(card);
                    }

                    reply.AttachmentLayout = AttachmentLayoutTypes.Carousel;
                    await context.PostAsync(reply);

                    skip += TakeSize;
                }

                context.Wait(this.CreateAsync);
            }
            else
            {
                context.Fail(new UnknownCommandException(activity.Text));
            }
        }

        /// <summary>
        /// Creates a new release for a release definition.
        /// </summary>
        /// <param name="context">A <see cref="IDialogContext"/>.</param>
        /// <param name="result">A <see cref="IMessageActivity"/>/</param>
        /// <returns>A <see cref="Task"/>.</returns>
        public async Task CreateAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            context.ThrowIfNull(nameof(context));
            result.ThrowIfNull(nameof(result));

            var activity = await result;
            var text = (activity.RemoveRecipientMention() ?? string.Empty).Trim().ToLowerInvariant();
            var reply = context.MakeMessage();

            var match = Regex.Match(text, CommandMatchCreate);
            if (match.Success)
            {
                var typing = context.MakeMessage();
                typing.Type = ActivityTypes.Typing;
                await context.PostAsync(typing);

                var definitionId = Convert.ToInt32(match.Groups[1].Value);
                var release = await this.VstsService.CreateReleaseAsync(this.Account, this.TeamProject, definitionId, this.Profile.Token);
                reply.Text = string.Format(Labels.ReleaseCreated, release.Id);

                await context.PostAsync(reply);
                context.Done(reply);
            }
            else
            {
                context.Fail(new UnknownCommandException(activity.Text));
            }
        }
    }
}