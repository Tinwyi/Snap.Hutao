// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.DependencyInjection.Annotation.HttpClient;
using Snap.Hutao.Service.Hutao;
using Snap.Hutao.Web.Endpoint.Hutao;
using Snap.Hutao.Web.Hutao.Response;
using Snap.Hutao.Web.Request.Builder;
using Snap.Hutao.Web.Request.Builder.Abstraction;
using System.Collections.Immutable;
using System.Net.Http;

namespace Snap.Hutao.Web.Hutao.GachaLog;

[ConstructorGenerated(ResolveHttpClient = true)]
[HttpClient(HttpClientConfiguration.Default)]
internal sealed partial class HomaGachaLogClient
{
    private readonly IHttpRequestMessageBuilderFactory httpRequestMessageBuilderFactory;
    private readonly IHutaoEndpointsFactory hutaoEndpointsFactory;
    private readonly HutaoUserOptions hutaoUserOptions;
    private readonly HttpClient httpClient;

    public async ValueTask<HutaoResponse<GachaEventStatistics>> GetGachaEventStatisticsAsync(CancellationToken token = default)
    {
        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogStatisticsCurrentEvents())
            .Get();

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse<GachaEventStatistics>? resp = await builder
            .SendAsync<HutaoResponse<GachaEventStatistics>>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    public async ValueTask<HutaoResponse<GachaDistribution>> GetGachaDistributionAsync(GachaDistributionType distributionType, CancellationToken token = default)
    {
        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogStatisticsDistribution(distributionType))
            .Get();

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse<GachaDistribution>? resp = await builder
            .SendAsync<HutaoResponse<GachaDistribution>>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    public async ValueTask<HutaoResponse<ImmutableArray<GachaEntry>>> GetGachaEntriesAsync(CancellationToken token = default)
    {
        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogEntries())
            .Get();

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse<ImmutableArray<GachaEntry>>? resp = await builder
            .SendAsync<HutaoResponse<ImmutableArray<GachaEntry>>>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    public async ValueTask<HutaoResponse<EndIds>> GetEndIdsAsync(string uid, CancellationToken token = default)
    {
        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogEndIds(uid))
            .Get();

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse<EndIds>? resp = await builder
            .SendAsync<HutaoResponse<EndIds>>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    public async ValueTask<HutaoResponse<ImmutableArray<GachaItem>>> RetrieveGachaItemsAsync(string uid, EndIds endIds, CancellationToken token = default)
    {
        UidAndEndIds uidAndEndIds = new(uid, endIds);

        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogRetrieve())
            .PostJson(uidAndEndIds);

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse<ImmutableArray<GachaItem>>? resp = await builder
            .SendAsync<HutaoResponse<ImmutableArray<GachaItem>>>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    public async ValueTask<HutaoResponse> UploadGachaItemsAsync(string uid, IReadOnlyList<GachaItem> gachaItems, CancellationToken token = default)
    {
        UidAndItems uidAndItems = new(uid, gachaItems);

        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogUpload())
            .PostJson(uidAndItems);

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse? resp = await builder
            .SendAsync<HutaoResponse>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    public async ValueTask<HutaoResponse> DeleteGachaItemsAsync(string uid, CancellationToken token = default)
    {
        HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory.Create()
            .SetRequestUri(hutaoEndpointsFactory.Create().GachaLogDelete(uid))
            .Get();

        await builder.TrySetTokenAsync(hutaoUserOptions).ConfigureAwait(false);

        HutaoResponse? resp = await builder
            .SendAsync<HutaoResponse>(httpClient, token)
            .ConfigureAwait(false);

        return Web.Response.Response.DefaultIfNull(resp);
    }

    private sealed class UidAndEndIds
    {
        public UidAndEndIds(string uid, EndIds endIds)
        {
            Uid = uid;
            EndIds = endIds;
        }

        public string Uid { get; }

        public EndIds EndIds { get; }
    }

    private sealed class UidAndItems
    {
        public UidAndItems(string uid, IReadOnlyList<GachaItem> gachaItems)
        {
            Uid = uid;
            Items = gachaItems;
        }

        public string Uid { get; }

        public IReadOnlyList<GachaItem> Items { get; }
    }
}