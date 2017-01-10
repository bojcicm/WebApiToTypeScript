﻿using System;
using System.Linq;
using WebApiToTypeScript.Block;
using WebApiToTypeScript.WebApi;

namespace WebApiToTypeScript.Endpoints
{
    public class AngularEndpointsService : ServiceAware
    {
        public TypeScriptBlock CreateServiceBlock()
        {
            var serviceBlock = new TypeScriptBlock($"{Config.NamespaceOrModuleName} {Config.ServiceNamespace}")
                .AddAndUseBlock($"export class {Config.ServiceName}")
                .AddStatement("static $inject = ['$http', '$q'];")
                .AddStatement("static $http: ng.IHttpService;")
                .AddStatement("static $q: ng.IQService;")
                .AddStatement("static endpointCache = {};")
                .AddAndUseBlock("constructor($http: ng.IHttpService, $q: ng.IQService)")
                .AddStatement($"{Config.ServiceName}.$http = $http;")
                .AddStatement($"{Config.ServiceName}.$q = $q;")
                .Parent
                .AddAndUseBlock("static call<TView>(endpoint: IEndpoint, data)")
                .AddAndUseBlock($"var call = {Config.ServiceName}.$http<TView>(", isFunctionBlock: true, terminationString: ";")
                .AddStatement("method: endpoint._verb,")
                .AddStatement("url: endpoint.toString(),")
                .AddStatement("data: data")
                .Parent
                .AddStatement("return call.then(response => response.data);");

            if (Config.EndpointsSupportCaching)
                serviceBlock
                    .Parent
                    .AddAndUseBlock("static callCached<TView>(endpoint: IEndpoint, data)")
                    .AddStatement("var cacheKey = endpoint.toString();")
                    .AddAndUseBlock("if (this.endpointCache[cacheKey] == null)")
                    .AddAndUseBlock("return this.call<TView>(endpoint, data).then(result =>", isFunctionBlock: true,
                        terminationString: ";")
                    .AddStatement("this.endpointCache[cacheKey] = result;")
                    .AddStatement("return this.endpointCache[cacheKey];")
                    .Parent
                    .Parent
                    .AddStatement("const deferred = this.$q.defer();")
                    .AddStatement("deferred.resolve(this.endpointCache[cacheKey]);")
                    .AddStatement("return deferred.promise;");

            return serviceBlock
                    .Parent
                    .Parent;
        }

        public void WriteServiceObjectToBlock(TypeScriptBlock serviceBlock, WebApiController webApiController)
        {
            TypeScriptBlock controllerBlock = null;

            if (Config.GenerateService)
            {
                controllerBlock = serviceBlock
                    .AddAndUseBlock($"public {webApiController.Name}: {Config.EndpointsNamespace}.{webApiController.Name}.I{webApiController.Name}Service =");
            }
            else
            {
                controllerBlock = serviceBlock
                    .AddAndUseBlock($"public {webApiController.Name} =");
            }

            var actions = webApiController.Actions;

            for (var a = 0; a < actions.Count; a++)
            {
                var action = actions[a];

                var constructorParameterMappings = action.GetConstructorParameterMappings();

                for (var v = 0; v < action.Verbs.Count; v++)
                {
                    var verb = action.Verbs[v];

                    var actionName = action.GetActionNameForVerb(verb);

                    var isLastActionAndVerb = a == actions.Count - 1
                        && v == action.Verbs.Count - 1;

                    var areAllParametersOptional = constructorParameterMappings
                        .All(m => m.IsOptional);

                    var optionalString = areAllParametersOptional
                        ? "?"
                        : string.Empty;

                    var callArgumentDefinition = action.GetCallArgumentDefinition(verb);
                    var callArgumentValue = action.GetCallArgumentValue(verb);

                    var interfaceFullName = $"{Config.EndpointsNamespace}.{webApiController.Name}.I{actionName}";
                    var interfaceWithCallFullName = $"{Config.EndpointsNamespace}.{webApiController.Name}.I{actionName}WithCall";
                    var endpointFullName = $"{Config.EndpointsNamespace}.{webApiController.Name}.{actionName}";

                    var endpointExtendBlock = controllerBlock
                        .AddAndUseBlock
                        (
                            outer: $"{actionName}: (args{optionalString}: {interfaceFullName}): {interfaceWithCallFullName} =>",
                            isFunctionBlock: false,
                            terminationString: !isLastActionAndVerb ? "," : string.Empty
                        )
                        .AddStatement($"var endpoint = new {endpointFullName}(args);")
                        .AddAndUseBlock("return _.extendOwn(endpoint,", isFunctionBlock: true, terminationString: ";")
                        .AddAndUseBlock($"call<TView>({callArgumentDefinition})", isFunctionBlock: false, terminationString: ",")
                        .AddStatement($"return {Config.ServiceName}.call<TView>(this, {callArgumentValue});")
                        .Parent;

                    if (Config.EndpointsSupportCaching && verb == WebApiHttpVerb.Get)
                        endpointExtendBlock.AddAndUseBlock($"callCached<TView>({callArgumentDefinition})")
                            .AddStatement($"return {Config.ServiceName}.callCached<TView>(this, {callArgumentValue});");
                }
            }
        }
    }
}