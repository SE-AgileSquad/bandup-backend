using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BandUp.Api.Endpoints.Writing;

internal sealed class WritingOpenApiFilter : IOperationFilter
{
    internal const string SessionScheme = "WritingSession";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (!metadata.OfType<PrivateWritingResponse>().Any())
        {
            return;
        }

        operation.Description = "Requires an authenticated BandUp cookie session. Private responses use Cache-Control: no-store.";
        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SessionScheme, context.Document)] = [],
        });
        if (context.ApiDescription.HttpMethod == HttpMethods.Post)
        {
            DescribeSubmission(operation);
        }
        else if (metadata.OfType<IEndpointNameMetadata>().Any(endpoint => endpoint.EndpointName == "Writing_ListSubmissions"))
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(PageParameter("page", 1, "Page number. Values below 1 become 1."));
            operation.Parameters.Add(PageParameter("pageSize", 20, "Items per page. Values are clamped to 1–100."));
        }
    }

    private static void DescribeSubmission(OpenApiOperation operation)
    {
        operation.Description += " Acceptance is disabled by default. A replay with the same key and exact payload returns the original receipt.";
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            In = ParameterLocation.Header, Name = "Idempotency-Key", Required = true,
            Description = "One non-empty UUID in hyphenated form, scoped to the authenticated learner.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
        });
        operation.Parameters.Add(new OpenApiParameter
        {
            In = ParameterLocation.Header, Name = "X-Requested-With", Required = true,
            Description = "Send bandup to satisfy the cookie-session CSRF check.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        });
        // Describe manual binding without adding an Accepts matcher ahead of auth and CSRF.
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "UTF-8 JSON up to 65,536 bytes. Duplicate and unknown properties are rejected.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new()
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object, Required = new HashSet<string> { "promptId", "essayText" },
                        AdditionalPropertiesAllowed = false,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["promptId"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                            ["essayText"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 8000,
                                Description = "Original essay text, 100–350 words." },
                        },
                    },
                },
            },
        };
        if (operation.Responses?["202"] is OpenApiResponse accepted)
        {
            accepted.Headers ??= new Dictionary<string, IOpenApiHeader>();
            accepted.Headers["Location"] = new OpenApiHeader
            {
                Description = "Relative URL of the accepted submission's status resource.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            };
        }
    }

    private static OpenApiParameter PageParameter(string name, int defaultValue, string description) => new()
    {
        In = ParameterLocation.Query, Name = name, Description = description,
        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Default = JsonValue.Create(defaultValue) },
    };
}
