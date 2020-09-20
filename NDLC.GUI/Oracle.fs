namespace NDLC.GUI

open FSharp.Control.Tasks

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Presenters
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling

open Elmish

open Avalonia.FuncUI.Components
open Avalonia.FuncUI.DSL

open NDLC.Infrastructure
open NDLC.Secp256k1

open System
open System.Diagnostics
open Avalonia.Media
open NBitcoin
open NBitcoin.DataEncoders
open NBitcoin.Secp256k1
open NDLC.GUI.Utils
            
module OracleModule =
    open NDLC.GUI.Utils
    module Constants =
        [<Literal>]
        let defaultOracleName = "MyOwesomeOracleName"
    type OracleInfo = {
        Name: string
        OracleId: OracleId option
        KeyPath: RootedKeyPath option
    }
        with
        static member Empty = {
            Name = ""
            OracleId = None
            KeyPath = None
        }
    type State =
        {
            KnownOracles: Deferred<Result<OracleInfo seq, string>>
            Selected: OracleInfo option
            ImportingOracle: OracleInfo option
            InvalidOracleErrorMsg: string option
        }
    type Msg =
        | LoadOracles of AsyncOperationStatus<Result<OracleInfo seq, string>>
        | Remove of OracleInfo
        | Select of OracleInfo option
        | Generate of name: string
        | InvalidOracle of errorMsg: string
        | ToggleOracleImport
        | NewOracle of OracleInfo
        
    let loadOracleInfos(repo: NameRepository) =
        task {
            let! a =
                repo.AsOracleNameRepository().GetIds()
            return
                a
                |> Seq.sortBy(fun kv -> kv.Key)
                |> Seq.map(fun kv -> { OracleInfo.Name = kv.Key; OracleId = Some kv.Value; KeyPath = None })
                |> Ok
                |> Finished
                |> LoadOracles
        }
        
    let removeOracle (repo: NameRepository) oracle = task {
        let! isOk = repo.RemoveMapping(Scopes.Oracles, oracle.Name)
        if (not <| isOk) then raise <| Exception("Failed to remove oracle! This should never happen") else
        return ()
    }
    let init =
        { KnownOracles = Deferred.HasNotStartedYet
          Selected = None
          ImportingOracle = None
          InvalidOracleErrorMsg = None
          }, Cmd.ofMsg(LoadOracles Started)
        
    let update (globalConfig) (msg: Msg) (state: State) =
        match msg with
        | LoadOracles Started ->
            let nameRepo =  (ConfigUtils.nameRepo globalConfig)
            { state with KnownOracles = InProgress }, Cmd.OfTask.result (loadOracleInfos nameRepo)
        | LoadOracles (Finished (Ok oracles)) ->
            { state with KnownOracles = Resolved(Ok oracles) }, Cmd.none
        | LoadOracles (Finished (Error e)) ->
            {state with KnownOracles = Resolved(Error e)} , Cmd.none
        | Remove oracle ->
            let nameRepo = ConfigUtils.nameRepo globalConfig
            (removeOracle nameRepo oracle).GetAwaiter().GetResult()
            let newOracles = state.KnownOracles |> Deferred.map(Result.map(Seq.where(fun o -> o <> oracle)))
            { state with KnownOracles = newOracles }, Cmd.none
        | Select o ->
            {state with Selected = o}, Cmd.none
        | Generate oracleName ->
            let generate() =
                task {
                    let p =
                        state.KnownOracles
                        |> Deferred.map(Result.map(Seq.exists(fun o -> o.Name = Constants.defaultOracleName)))
                    match p with
                    | Deferred.Resolved (Ok true) ->
                        return (InvalidOracle(sprintf "Please change the oracle name from \"%s\" before you go next" Constants.defaultOracleName) )
                    | _ ->
                    let o = (ConfigUtils.tryGetOracle globalConfig oracleName).GetAwaiter().GetResult()
                    match o with
                    | Some _ -> return (InvalidOracle "Oracle with the same name Already exists!")
                    | None ->
                    let repo = ConfigUtils.repository globalConfig
                    let nameRepo = ConfigUtils.nameRepo globalConfig
                    let! (keyPath, key) = repo.CreatePrivateKey()
                    let pubkey,_ = key.PubKey.ToECPubKey().ToXOnlyPubKey()
                    let pubkeyHex = Encoders.Hex.EncodeData(pubkey.ToBytes())
                    match OracleId.TryParse pubkeyHex with
                    | true, oracleId ->
                        return
                            { OracleId = Some oracleId; Name = oracleName; KeyPath = None }
                            |> Msg.NewOracle
                    | false, _ ->
                        return (InvalidOracle "Failed to parse Oracle id!")
                }
            state, Cmd.OfTask.either generate () id (fun ex -> Msg.InvalidOracle (ex.Message))
        | ToggleOracleImport ->
            { state with ImportingOracle = OracleInfo.Empty |> Some }, Cmd.none
        | NewOracle oracle ->
            Debug.Assert(oracle.OracleId.IsSome)
            let saveOracle () =
                task {
                    let repo = ConfigUtils.repository globalConfig
                    let nameRepo = ConfigUtils.nameRepo globalConfig
                    do! nameRepo.SetMapping(Scopes.Oracles, oracle.Name, oracle.OracleId.Value.ToString())
                    match oracle.KeyPath with
                    | Some keyPath ->
                        let! _ = repo.AddOracle(oracle.OracleId.Value.PubKey, keyPath)
                        ()
                    | None -> ()
                    return ()
                }
            let newOracles = state.KnownOracles |> Deferred.map(Result.map(Seq.append (Seq.singleton oracle)))
            { state with KnownOracles = newOracles}, Cmd.OfTask.attempt saveOracle () (fun x -> InvalidOracle(x.Message))
        | InvalidOracle msg ->
            { state with InvalidOracleErrorMsg = Some msg }, Cmd.none
        
    let oracleListView (oracles: OracleInfo seq) dispatch =
        ListBox.create [
            ListBox.onSelectedItemChanged(fun obj ->
                match obj with
                | :? OracleInfo as o -> o |> Some
                | _ -> None
                |> Select |> dispatch
            )
            ListBox.dataItems (oracles)
            ListBox.itemTemplate
                (DataTemplateView<OracleInfo>.create (fun d ->
                DockPanel.create [
                    DockPanel.lastChildFill false
                    DockPanel.children [
                        TextBlock.create [
                            TextBlock.text d.Name
                            TextBlock.margin 5.
                        ]
                        TextBlock.create [
                            TextBlock.text (d.OracleId |> function Some x -> x.ToString() | None -> "")
                            TextBlock.margin 5.
                        ]
                        Button.create [
                            Button.dock Dock.Right
                            Button.content "remove"
                            Button.onClick ((fun _ -> d |> Msg.Remove |> dispatch), SubPatchOptions.OnChangeOf d.OracleId)
                        ]
                    ]
                ]
            ))
        ]
        
        
    let spinner =
        StackPanel.create [
            StackPanel.children [
                StackPanel.create [
                    StackPanel.horizontalAlignment HorizontalAlignment.Center
                    StackPanel.verticalAlignment VerticalAlignment.Center
                    StackPanel.children [
                        TextBox.create [
                            TextBox.text "Now loading..."
                        ]
                    ]
                ]
            ]
        ]
    let renderError (errorMsg: string) =
        StackPanel.create [
            StackPanel.children [
                    StackPanel.create [
                    StackPanel.dock Dock.Top
                    StackPanel.verticalAlignment VerticalAlignment.Center
                    StackPanel.children [
                        TextBlock.create [
                            TextBlock.dock Dock.Top
                            TextBlock.classes ["h1"]
                            TextBlock.fontSize 24.
                            TextBlock.fontWeight FontWeight.Thin
                            TextBlock.text errorMsg
                        ]
                    ]
                ]
            ]
        ]
        
    let createOracleButton (dispatch: Msg -> unit) =
        StackPanel.create [
            StackPanel.dock Dock.Bottom
            StackPanel.orientation Orientation.Horizontal
            StackPanel.children[
                Button.create [
                    Button.content "Import"
                    Button.margin 4.
                    Button.fontSize 15.
                    Button.classes ["round"]
                    Button.onClick(fun _ -> dispatch ToggleOracleImport)
                ]
                Button.create [
                    Button.content "Generate"
                    Button.margin 4.
                    Button.fontSize 15.
                    Button.classes ["round"; "add"]
                    Button.onClick(fun _ -> dispatch (Generate "MyNewOracle"))
                    Button.styles (
                         let styles = Styles()
                         let style = Style(fun x -> x.OfType<Button>().Template().OfType<ContentPresenter>())
                         
                         let setter = Setter(ContentPresenter.CornerRadiusProperty, CornerRadius(10.0))
                         style.Setters.Add setter
                         
                         styles.Add style
                         styles
                    )
                ]
            ]
        ]
    let viewOracle dispatch state =
        let o = state.KnownOracles
        match o with
        | HasNotStartedYet -> StackPanel.create []
        | InProgress -> spinner
        | Resolved(Ok items) ->
            StackPanel.create [
                StackPanel.orientation Orientation.Vertical
                StackPanel.children [
                    TextBlock.create [
                        TextBlock.dock Dock.Top
                        TextBlock.margin 5.
                        TextBlock.fontSize 18.
                        TextBlock.text (sprintf "List of known oracles: (count: %i)" (items |> Seq.length))
                    ]
                    oracleListView items dispatch
                    createOracleButton dispatch
                    match state.InvalidOracleErrorMsg with
                    | None -> ()
                    | Some x ->
                        TextBlock.create[
                            TextBlock.text x
                            TextBlock.classes ["error"]
                        ]
                ]
            ]
        | Resolved(Error e) ->
            renderError e
    
    let oracleDetailsView (state: OracleInfo option) =
        DockPanel.create [
            DockPanel.isVisible state.IsSome
            DockPanel.width 250.
            DockPanel.children [
                match state with
                | None -> ()
                | Some o ->
                    StackPanel.create [
                        StackPanel.dock Dock.Top
                        StackPanel.orientation Orientation.Vertical
                        StackPanel.margin 10.
                        StackPanel.children [
                            TextBlock.create [
                                TextBlock.text "Here goes oracle details"
                            ]
                            TextBlock.create [
                                TextBlock.text (sprintf "%A" o)
                            ]
                        ]
                    ]
            ]
        ]
    let view (state: State) dispatch =
        DockPanel.create [
            DockPanel.children [
                viewOracle dispatch state
                oracleDetailsView state.Selected
            ]
        ]
        
