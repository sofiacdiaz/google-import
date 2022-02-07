using GraphQL;
using GraphQL.Types;
using SheetsCatalogImport.Data;
using SheetsCatalogImport.Services;

namespace SheetsCatalogImport.GraphQL
{
    [GraphQLMetadata("Mutation")]
    public class Mutation : ObjectGraphType<object>
    {
        public Mutation(IGoogleSheetsService googleSheetsService, ISheetsCatalogImportRepository sheetsCatalogImportRepository, IVtexApiService vtexApiService)
        {
            Name = "Mutation";

            Field<BooleanGraphType>(
                "revokeToken",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "accountName", Description = "Account Name" }
                ),
                resolve: context =>
                {
                    bool revoked = googleSheetsService.RevokeGoogleAuthorizationToken().Result;
                    if (revoked)
                    {
                        string accountName = context.GetArgument<string>("accountName");
                        sheetsCatalogImportRepository.SaveFolderIds(null, accountName);
                    }

                    return revoked;
                });

            Field<StringGraphType>(
                "googleAuthorize",
                resolve: context =>
                {
                    return googleSheetsService.GetAuthUrl();
                });

            Field<StringGraphType>(
                "createSheet",
                resolve: context =>
                {
                    var created = googleSheetsService.CreateSheet();
                    vtexApiService.SetBrandList();
                    return created;
                });

            Field<StringGraphType>(
                "processSheet",
                resolve: context =>
                {
                    return vtexApiService.ProcessSheet();
                });

            Field<StringGraphType>(
                "clearSheet",
                resolve: context =>
                {
                    var cleared = vtexApiService.ClearSheet();
                    var catalogAndBrand = vtexApiService.SetBrandList();
                    return !string.IsNullOrWhiteSpace(cleared.Result) && catalogAndBrand.Result;
                });

            Field<StringGraphType>(
                "addImages",
                resolve: context =>
                {
                    return vtexApiService.AddImagesToSheet();
                });

            Field<StringGraphType>(
                "exportProducts",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "exportQuery", Description = "Export Query" }
                ),
                resolve: context =>
                {
                    string query = context.GetArgument<string>("exportQuery");
                    return vtexApiService.ExportToSheet(query);
                });
        }
    }
}