namespace Fable.WebWorker

open System
open System.Collections.Generic
open Fable.Core
open Fable.Core.JsInterop
open Fable.Import
open Thoth.Json

type IWebWorker =
    interface end

type WorkerRequest =
    /// * referenceExtraSuffix: e.g. add .txt extension to enable gzipping in Github Pages
    | CreateChecker of fableStandaloneUrl: string * refsDirUrl: string * extraRefs: string[] * refsExtraSuffix: string option * libJsonUrl: string option
    | ParseCode of fsharpCode: string
    | CompileCode of fsharpCode: string * optimize: bool
    | GetTooltip of id: Guid * line: int * column: int * lineText: string
    | GetCompletions of id: Guid * line: int * column: int * lineText: string
    | GetDeclarationLocation of id: Guid * line: int * column: int * lineText: string
    static member Decoder =
        Decode.Auto.generateDecoder<WorkerRequest>()

type CompileStats =
    { FCS_checker : float
      FCS_parsing : float
      Fable_transform : float
      Babel_generation : float }

type WorkerAnswer =
    | Loaded
    | LoadFailed
    | ParsedCode of errors: Fable.Standalone.Error[]
    | CompilationFinished of jsCode: string * errors: Fable.Standalone.Error[] * stats: CompileStats
    | CompilerCrashed of message: string
    | FoundTooltip of id: Guid * lines: string[]
    | FoundCompletions of id: Guid * Fable.Standalone.Completion[]
    | FoundDeclarationLocation of id: Guid * (* line1, col1, line2, col2 *) (int * int * int * int) option
    static member Decoder =
        Decode.Auto.generateDecoder<WorkerAnswer>()

type ObservableWorker<'InMsg>(worker: IWebWorker, decoder: Decode.Decoder<'InMsg>, ?name: string) =
    let name = defaultArg name "FABLE WORKER"
    let listeners = new Dictionary<Guid, IObserver<'InMsg>>()
    do worker?addEventListener("message", fun ev ->
        match ev?data: obj with
        | :? string as msg when not(String.IsNullOrEmpty(msg)) ->
            match Decode.fromString decoder msg with
            | Ok msg ->
                // Browser.console.log("[" + name + "] Received:", msg)
                for listener in listeners.Values do
                    listener.OnNext(msg)
            | Error err -> JS.console.error("[" + name + "] Cannot decode:", err)
        | _ -> ())
    member __.HasListeners =
        listeners.Count > 0
    member __.Post msg =
        worker?postMessage(Encode.Auto.toString(0, msg))
    member this.PostAndAwaitResponse(msg, picker) =
        Async.FromContinuations(fun (cont, err, cancel) ->
            let mutable disp = Unchecked.defaultof<IDisposable>
            disp <- this |> Observable.subscribe(fun msg ->
                match picker msg with
                | Some res ->
                    disp.Dispose()
                    cont res
                | None -> ())
            worker?postMessage(Encode.Auto.toString(0, msg))
        )
    member __.Subscribe obs =
        let id = Guid.NewGuid()
        listeners.Add(id, obs)
        { new IDisposable with
            member __.Dispose() = listeners.Remove(id) |> ignore }
    interface IObservable<'InMsg> with
        member this.Subscribe obs = this.Subscribe(obs)
