using GraphQL;
using GraphQL.Types;
using SheetsCatalogImport.Data;
using SheetsCatalogImport.Services;

namespace SheetsCatalogImport.GraphQL
{
    [GraphQLMetadata("Mutation")]
    public class Mutation : ObjectGraphType<object>
    {
        public Mutation(IGoogleSheetsService googleSheetsService, ISheetsCatalogImportRepository sheetsCatalogImportRepository, IVtexAPIService vtexAPIService)
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
                    var catalogAndBrand = vtexAPIService.SetBrandList();
                    return created;
                });

            Field<StringGraphType>(
                "processSheet",
                resolve: context =>
                {
                    return vtexAPIService.ProcessSheet();
                });

            Field<StringGraphType>(
                "clearSheet",
                resolve: context =>
                {
                    var cleared = vtexAPIService.ClearSheet();
                    var catalogAndBrand = vtexAPIService.SetBrandList();
                    return !string.IsNullOrWhiteSpace(cleared.Result) && catalogAndBrand.Result;
                });

            Field<StringGraphType>(
                "addImages",
                resolve: context =>
                {
                    return vtexAPIService.AddImagesToSheet();
                });

            Field<StringGraphType>(
                "exportProducts",
                arguments: new QueryArguments(
                    new QueryArgument<StringGraphType> { Name = "exportQuery", Description = "Export Query" }
                ),
                resolve: context =>
                {
                    string query = context.GetArgument<string>("exportQuery");
                    return vtexAPIService.ExportToSheet(query);
                });
        }
    }
}