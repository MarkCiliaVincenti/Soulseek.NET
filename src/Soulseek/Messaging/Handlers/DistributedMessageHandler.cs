﻿// <copyright file="DistributedMessageHandler.cs" company="JP Dillingham">
//     Copyright (c) JP Dillingham. All rights reserved.
//
//     This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as
//     published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
//     of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License along with this program. If not, see https://www.gnu.org/licenses/.
// </copyright>

namespace Soulseek.Messaging.Handlers
{
    using System;
    using System.Threading;
    using Soulseek.Messaging;
    using Soulseek.Messaging.Messages;
    using Soulseek.Network;

    internal sealed class DistributedMessageHandler : IDistributedMessageHandler
    {
        public DistributedMessageHandler(
            SoulseekClient soulseekClient,
            IDiagnosticFactory diagnosticFactory = null)
        {
            SoulseekClient = soulseekClient ?? throw new ArgumentNullException(nameof(soulseekClient));
            Diagnostic = diagnosticFactory ??
                new DiagnosticFactory(this, SoulseekClient.Options.MinimumDiagnosticLevel, (e) => DiagnosticGenerated?.Invoke(this, e));
        }

        public event EventHandler<DiagnosticGeneratedEventArgs> DiagnosticGenerated;

        private IDiagnosticFactory Diagnostic { get; }
        private SoulseekClient SoulseekClient { get; }

        public async void HandleParentMessage(object sender, byte[] message)
        {
            var connection = (IMessageConnection)sender;
            var code = new MessageReader<MessageCode.Distributed>(message).ReadCode();

            //Diagnostic.Debug($"Distributed message received: {code} from {connection.Username} ({connection.IPAddress}:{connection.Port})");

            try
            {
                switch (code)
                {
                    case MessageCode.Distributed.SearchRequest:
                        var searchRequest = DistributedSearchRequest.FromByteArray(message);
                        SearchResponse searchResponse;

                        SoulseekClient.DistributedConnectionManager.BroadcastAsync(message).Forget();

                        try
                        {
                            searchResponse = await SoulseekClient.Options.SearchResponseResolver(searchRequest.Username, searchRequest.Token, searchRequest.Query).ConfigureAwait(false);

                            if (searchResponse != null && searchResponse.FileCount > 0)
                            {
                                var (ip, port) = await SoulseekClient.GetUserAddressAsync(searchRequest.Username).ConfigureAwait(false);

                                var peerConnection = await SoulseekClient.PeerConnectionManager.GetOrAddMessageConnectionAsync(searchRequest.Username, ip, port, CancellationToken.None).ConfigureAwait(false);
                                await peerConnection.WriteAsync(searchResponse.ToByteArray()).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Diagnostic.Warning($"Error resolving search response for query '{searchRequest.Query}' requested by {searchRequest.Username} with token {searchRequest.Token}: {ex.Message}", ex);
                        }

                        break;

                    case MessageCode.Distributed.Ping:
                        Diagnostic.Debug($"PING?");
                        var pingResponse = new PingResponse(SoulseekClient.GetNextToken());
                        await connection.WriteAsync(pingResponse.ToByteArray()).ConfigureAwait(false);
                        Diagnostic.Debug($"PONG!");
                        break;

                    case MessageCode.Distributed.BranchLevel:
                        var branchLevel = DistributedBranchLevel.FromByteArray(message);
                        await SoulseekClient.DistributedConnectionManager.SetBranchLevel(branchLevel.Level).ConfigureAwait(false);
                        break;

                    case MessageCode.Distributed.BranchRoot:
                        var branchRoot = DistributedBranchRoot.FromByteArray(message);
                        await SoulseekClient.DistributedConnectionManager.SetBranchRoot(branchRoot.Username).ConfigureAwait(false);
                        break;

                    default:
                        Diagnostic.Debug($"Unhandled distributed message: {code} from {connection.Username} ({connection.IPAddress}:{connection.Port}); {message.Length} bytes");
                        break;
                }
            }
            catch (Exception ex)
            {
                Diagnostic.Warning($"Error handling distributed message: {code} from {connection.Username} ({connection.IPAddress}:{connection.Port}); {ex.Message}", ex);
            }
        }

        public async void HandleChildMessage(object sender, byte[] message)
        {
            var connection = (IMessageConnection)sender;
            var code = new MessageReader<MessageCode.Distributed>(message).ReadCode();

            Diagnostic.Debug($"Distributed message received: {code} from {connection.Username} ({connection.IPAddress}:{connection.Port})");

            try
            {
                switch (code)
                {
                    case MessageCode.Distributed.ChildDepth:
                        var childDepth = DistributedChildDepth.FromByteArray(message);
                        // not sure what to do with this.
                        Diagnostic.Debug($"Distributed child depth from {connection.Username}: {childDepth.Depth}");
                        break;

                    case MessageCode.Distributed.Ping:
                        Diagnostic.Debug($"PING?");
                        var pingResponse = new PingResponse(SoulseekClient.GetNextToken());
                        await connection.WriteAsync(pingResponse.ToByteArray()).ConfigureAwait(false);
                        Diagnostic.Debug($"PONG!");
                        break;

                    default:
                        Diagnostic.Debug($"Unhandled distributed message: {code} from {connection.Username} ({connection.IPAddress}:{connection.Port}); {message.Length} bytes");
                        break;
                }
            }
            catch (Exception ex)
            {
                Diagnostic.Warning($"Error handling distributed message: {code} from {connection.Username} ({connection.IPAddress}:{connection.Port}); {ex.Message}", ex);
            }
        }
    }
}
