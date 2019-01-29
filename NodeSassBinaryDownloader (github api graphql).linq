<Query Kind="Program">
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <NuGetReference>GraphQL.Client</NuGetReference>
  <Namespace>GraphQL</Namespace>
  <Namespace>GraphQL.Client</Namespace>
  <Namespace>GraphQL.Common.Request</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>Newtonsoft.Json.Linq</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Net.Http</Namespace>
  <Namespace>System.Net.Http.Headers</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
</Query>

async Task Main()
{
    /* GitHub API Access
     *
     * How to get a access token:
     * https://help.github.com/articles/creating-a-personal-access-token-for-the-command-line/
     *
     * scopes required:
     *   - public_repo
     */
    var authorizationToken = Util.GetPassword("GitHub Personal Access Token");

    // List of binaries to mirror
    var binaries = new[]
	{
		"linux_musl-x64-57_binding.node",
		"win32-x64-64_binding.node"
	};

    // Download location
    var rootPath = new DirectoryInfo(Util.ReadLine<string>("Enter download path"));
    if (!rootPath.Exists) throw new Exception($"Download location '{rootPath}' does not exist.");

    var handler = new HttpClientHandler();

    // Create GraphQL client
    var client = new GraphQLClient("https://api.github.com/graphql", new GraphQLClientOptions { HttpMessageHandler = handler });
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authorizationToken);
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Environment.UserName, DateTime.Now.ToString("dd-MM-yyyy")));

    // Create HTTP client
    var httpClient = new HttpClient(handler);

    foreach (var binaryName in binaries)
	{
		Console.WriteLine($"Querying {client.EndPoint} for {binaryName} binaries");		
        
		// Query GitHub API for node-sass releases
		var binaryDownloads = await client.QueryReleaseAssets(binaryName);
		
        foreach (var download in binaryDownloads)
        {
            var mirrorName = $"{download.ReleaseName}/{binaryName}";

            var releaseFolder = new DirectoryInfo(Path.Combine(rootPath.FullName, download.ReleaseName));
            if (!releaseFolder.Exists) releaseFolder.Create();

            var binaryFile = new FileInfo(Path.Combine(releaseFolder.FullName, binaryName));
            if (binaryFile.Exists)
            {
                Console.WriteLine($"{mirrorName} already exists");
                continue;
            }

            Console.WriteLine($"Downloading {download.DownloadUrl} to {mirrorName}");
            await httpClient.DownloadFile(download.DownloadUrl, binaryFile.FullName);
            Console.WriteLine($"{mirrorName} file created");
        }
    }

    Console.WriteLine(Util.WithStyle("\nRecommend checking the downloads look correct", "font-size: 30px"));
    Console.WriteLine(new Hyperlinq("https://github.com/sass/node-sass/releases/"));
    Console.WriteLine(new Hyperlinq(rootPath.FullName));
}

public static class Query
{
    public async static Task DownloadFile(this HttpClient client, string url, string filePath)
    {
        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"{response.StatusCode} {response.ReasonPhrase} {await response.Content.ReadAsStringAsync()}");
        }

        var responseStream = await response.Content.ReadAsStreamAsync();
        using (var fileStream = File.Create(filePath))
        {
            responseStream.Seek(0, SeekOrigin.Begin);
            responseStream.CopyTo(fileStream);
        }
    }

    public static async Task<IEnumerable<(string ReleaseName, string DownloadUrl)>> QueryReleaseAssets(this GraphQLClient client, string binaryName)
    {
        var request = new GraphQLRequest
        {
            Query = @"
            query ReleaseDownloadUrl($binaryName: String) {
              repository(owner: ""sass"", name: ""node-sass"") {
                releases(last: 5) {
                  nodes {
                    name
                    releaseAssets(first: 1, name: $binaryName) {
                      nodes {
                        downloadUrl
                      }
                    }
                  }
                }
              }
            }",
            Variables = new
            {
                binaryName
            }
        };

        var response = await client.PostAsync(request);

		if (response.Data == null) { throw new Exception("Data is null, auth problem?"); }			
		
        var repository = response.GetDataFieldAs<RepositoryReleases>("repository");
        var binaries = new List<(string Version, string DownloadUrl)>();

        foreach (var release in repository.Releases)
        {
            foreach (var releaseAsset in release.ReleaseAssets)
            {
                binaries.Add((release.Name, releaseAsset.DownloadUrl));
            }
        }

        return binaries;
    }
}

public class RepositoryReleases
{
    public ReleaseCollection Releases { get; set; }
}

[JsonObject]
public class ReleaseCollection : IEnumerable<Release>
{
    public Release[] Nodes { get; set; }

	public IEnumerator<Release> GetEnumerator() => ((IEnumerable<Release>)Nodes).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Release>)Nodes).GetEnumerator();
}

public class Release
{
	public string Name { get; set; }
	public ReleaseAssetCollection ReleaseAssets { get; set; }
}

[JsonObject]
public class ReleaseAssetCollection : IEnumerable<ReleaseAsset>
{
	public ReleaseAsset[] Nodes { get; set; }

	public IEnumerator<ReleaseAsset> GetEnumerator() => ((IEnumerable<ReleaseAsset>)Nodes).GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<ReleaseAsset>)Nodes).GetEnumerator();
}

public class ReleaseAsset
{
	public string DownloadUrl { get; set; }
}