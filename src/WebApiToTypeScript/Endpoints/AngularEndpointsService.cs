﻿using System.Diagnostics;
using System.IO;
using System.Linq;
using WebApiToTypeScript.Block;
using WebApiToTypeScript.WebApi;

namespace WebApiToTypeScript.Endpoints
{
    public class AngularEndpointsService : ServiceAware, IEndpointsService
    {
        public TypeScriptBlock CreateServiceBlock()
        {
            Debug.Assert(Config.NoNamespacesOrModules, $"Angular service doesn't support {nameof(Config.NoNamespacesOrModules)} = false!");

            var block = new TypeScriptBlock($"{Config.NamespaceOrModuleName} {Config.ServiceNamespace}", suppressOuter: true);

            if (Config.GenerateInterfaces)
            {
                var relativePathToInterfacesFile = Helpers.GetRelativePath(Config.EndpointsOutputDirectory, Config.InterfacesOutputDirectory);
                var interfacesFileName = Path.GetFileNameWithoutExtension(Config.InterfacesFileName);

                block = block
                    .AddStatement($"import * as Interfaces from '{relativePathToInterfacesFile}/{interfacesFileName}';");
            }

            var relativePathToEndpointsFile = Helpers.GetRelativePath(Config.ServiceOutputDirectory, Config.EndpointsOutputDirectory);
            var endpointsFileName = Path.GetFileNameWithoutExtension(Config.EndpointsFileName);

            block = block
                .AddStatement($"import * as {Endpoints} from '{relativePathToEndpointsFile}/{endpointsFileName}';")
                .AddStatement("")
                .AddStatement("import { Injectable } from '@angular/core';")
                .AddStatement("import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';")
                .AddStatement("");

            var constructorBlock = block
                .AddStatement($"type BeforeCallHandler = (endpoint: {Endpoints}.{IEndpoint}, data, config: any) => Promise<void>;")
                .AddStatement($"type AfterCallHandler = <TView> (endpoint: {Endpoints}.{IEndpoint}, data, config: any, response: TView) => Promise<void>;")
                .AddAndUseBlock($"export class {Config.ServiceName}")
                .AddStatement("static $inject = ['$http', '$q'];")
                .AddStatement("static endpointCache = {};", condition: Config.EndpointsSupportCaching)
                .AddAndUseBlock("constructor(private $http: HttpClient, $q: ng.IQService)");

            var serviceBlock = constructorBlock
                .Parent
                .AddAndUseBlock($"static call<TView>(httpService: HttpClient, qService: ng.IQService, endpoint: {Endpoints}.{IEndpoint}, data, httpConfig?: ng.IRequestShortcutConfig)")
                .AddAndUseBlock("const config =")
                .AddStatement("method: endpoint._verb,")
                .AddStatement("url: endpoint.toString(),")
                .AddStatement("data: data")
                .Parent
                .AddStatement("httpConfig && _.extend(config, httpConfig);")
                .AddStatement("")
                .AddAndUseBlock($"return qService.all({Config.ServiceName}.onBeforeCallHandlers.map(onBeforeCall => onBeforeCall.handler(endpoint, data, config))).then(before =>", isFunctionBlock: true, terminationString: ";")
                .AddStatement($"const call = httpService<TView>(config);")
                .AddAndUseBlock("return call.then(response =>", isFunctionBlock: true, terminationString: ";")
                .AddStatement("let result = response.data;")
                .AddStatement($"return qService.all({Config.ServiceName}.onAfterCallHandlers.map(onAfterCall => onAfterCall.handler<TView>(endpoint, data, config, result))).then(after => result);")
                .Parent
                .Parent
                .Parent
                .AddStatement("private static onBeforeCallHandlers: ({ name: string; handler: BeforeCallHandler; })[] = []")
                .AddStatement("private static onAfterCallHandlers: ({ name: string; handler: AfterCallHandler; })[] = []")
                .AddAndUseBlock("static AddBeforeCallHandler = (name: string, handler: BeforeCallHandler) =>")
                .AddStatement($"{Config.ServiceName}.onBeforeCallHandlers = _.filter({Config.ServiceName}.onBeforeCallHandlers, h => h.name != name);")
                .AddStatement($"{Config.ServiceName}.onBeforeCallHandlers.push({{ name: name, handler: handler }});")
                .AddStatement($"return () => {Config.ServiceName}.onBeforeCallHandlers = _.filter({Config.ServiceName}.onBeforeCallHandlers, h => h.name != name);")
                .Parent
                .AddAndUseBlock("static AddAfterCallHandler = (name: string, handler: AfterCallHandler) =>")
                .AddStatement($"{Config.ServiceName}.onAfterCallHandlers = _.filter({Config.ServiceName}.onAfterCallHandlers, h => h.name != name);")
                .AddStatement($"{Config.ServiceName}.onAfterCallHandlers.push({{ name: name, handler: handler }});")
                .AddStatement($"return () => {Config.ServiceName}.onAfterCallHandlers = _.filter({Config.ServiceName}.onAfterCallHandlers, h => h.name != name);")
                .Parent;

            if (Config.EndpointsSupportCaching)
            {
                serviceBlock
                    .AddAndUseBlock($"static callCached<TView>(httpService: ng.IHttpService, qService: ng.IQService, endpoint: {Endpoints}.{IEndpoint}, data, httpConfig?: ng.IRequestShortcutConfig)")
                    .AddStatement("var cacheKey = endpoint.toString();")
                    .AddAndUseBlock("if (this.endpointCache[cacheKey] == null)")
                    .AddAndUseBlock("return this.call<TView>(httpService, qService, endpoint, data, httpConfig).then(result =>", isFunctionBlock: true, terminationString: ";")
                    .AddStatement("this.endpointCache[cacheKey] = result;")
                    .AddStatement("return this.endpointCache[cacheKey];")
                    .Parent
                    .Parent
                    .AddStatement("const deferred = qService.defer();")
                    .AddStatement("deferred.resolve(this.endpointCache[cacheKey]);")
                    .AddStatement("return deferred.promise;");
            }

            return serviceBlock
                .Parent;
        }

        public void WriteServiceObjectToBlock(TypeScriptBlock serviceBlock, WebApiController webApiController)
        {
            var constructorBlock = serviceBlock.Children
                .OfType<TypeScriptBlock>()
                .First();

            var endpointsPrefix = $"{Endpoints}";

            var controllerBlock = serviceBlock
                .AddStatement($"public {webApiController.Name}: {endpointsPrefix}.{webApiController.Name}.I{webApiController.Name}Service = <any>{{}};");

            var actions = webApiController.Actions;

            foreach (var action in actions)
            {
                if (action.BodyParameters.Count > 1)
                {
                    continue;
                }

                var constructorParameterMappings = action.GetConstructorParameterMappings();

                foreach (var verb in action.Verbs)
                {
                    var actionName = action.GetActionNameForVerb(verb);

                    var areAllParametersOptional = constructorParameterMappings
                        .All(m => m.IsOptional);

                    var optionalString = areAllParametersOptional
                        ? "?"
                        : string.Empty;

                    var callArgumentDefinition = action.GetCallArgumentDefinition(verb);
                    var callArgumentValue = action.GetCallArgumentValue(verb);

                    var interfaceFullName = $"{endpointsPrefix}.{webApiController.Name}.I{actionName}";
                    var interfaceWithCallFullName = $"{endpointsPrefix}.{webApiController.Name}.I{actionName}WithCall";
                    var endpointFullName = $"{endpointsPrefix}.{webApiController.Name}.{actionName}";

                    string typeScriptReturnType, typeScriptTypeForCall;

                    action.GetReturnTypes(out typeScriptReturnType, out typeScriptTypeForCall);

                    var endpointExtendBlock = constructorBlock
                        .AddAndUseBlock
                        (
                            outer: $"this.{webApiController.Name}.{actionName} = (args{optionalString}: {interfaceFullName}): {interfaceWithCallFullName} =>",
                            isFunctionBlock: false,
                            terminationString: ";"
                        )
                        .AddStatement($"var endpoint = new {endpointFullName}(args);")
                        .AddAndUseBlock("return _.extendOwn(endpoint,", isFunctionBlock: true, terminationString: ";")
                        .AddAndUseBlock
                        (
                            outer: $"call{typeScriptTypeForCall}({callArgumentDefinition})",
                            isFunctionBlock: false,
                            terminationString: Config.EndpointsSupportCaching ? "," : string.Empty
                        )
                        .AddStatement($"return {Config.ServiceName}.call{typeScriptReturnType}($http, $q, this, {callArgumentValue});")
                        .Parent;

                    if (Config.EndpointsSupportCaching && verb == WebApiHttpVerb.Get)
                    {
                        endpointExtendBlock.AddAndUseBlock($"callCached{typeScriptTypeForCall}({callArgumentDefinition})")
                            .AddStatement($"return {Config.ServiceName}.callCached{typeScriptReturnType}($http, $q, this, {callArgumentValue});");
                    }
                }
            }
        }

        public string GetAdditionalCallArguments()
        {
            return $"httpHeaders?: HttpHeaders";
        }
    }
}