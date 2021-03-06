module FunScript.Core.Web

open System
open System.Web
open FunScript

module Literals =
    [<Literal>]
    let getBytesJS = """var str = {1};
    var byteArray = [];
    for (var i = 0; i < str.length; i++)
        if (str.charCodeAt(i) <= 0x7F)
            byteArray.push(str.charCodeAt(i));
        else {
            var h = encodeURIComponent(str.charAt(i)).substr(1).split('%');
            for (var j = 0; j < h.length; j++)
                byteArray.push(parseInt(h[j], 16));
        }
    return byteArray;"""

    [<Literal>]
    let getStringJS = """var byteArray = {1};
    var str = '';
    for (var i = 0; i < byteArray.length; i++)
        str +=  byteArray[i] <= 0x7F?
                byteArray[i] === 0x25 ? "%25" : // %
                String.fromCharCode(byteArray[i]) :
                "%" + byteArray[i].toString(16).toUpperCase();
    return decodeURIComponent(str);"""

    [<Literal>]
    let sendRequestJS = """
    var _method = {0}, _url = {1}, _headerKeys = {2}, _headerValues = {3}, _onSuccess = {4}, _onError = {5};

    if (window.XDomainRequest) {
        var req = new XDomainRequest();
        req.onload = function() { _onSuccess(req.responseText); };
        req.onerror = _onError;
        req.ontimeout = _onError;
        req.timeout = 10000;
        req.open(_method, _url);
        req.send();
    }
    else {
        var req;

        if (window.XMLHttpRequest)
          req = new XMLHttpRequest();
        else
          req = new ActiveXObject("Microsoft.XMLHTTP");

        req.onreadystatechange = function () {
            if(req.readyState == 4) {
                if(req.status == 200 || req.status == 204) {
                    _onSuccess(req.responseText);
                }
                else {
                    _onError();
                }
            }
        };
        req.open(_method, _url, true);
        for(var i = 0; i < _headerKeys.length; i++) {
            var key = _headerKeys[i];
            var value = _headerValues[i];
            req.setRequestHeader(key, value);
        }
        req.send();
    }"""


/// Note: XDomainRequest doesn't support headers!
[<JS; JSEmit(Literals.sendRequestJS)>]
let sendRequest(meth : string, url : string, headerKeys : string[], headerValues : string[], onSuccess : string -> unit, onError : unit -> unit) =
    failwith "never"

[<JS>]
type UTF8Encoding() =
    
    [<JSEmit(Literals.getBytesJS)>]
    member __.GetBytes(text : string) : byte[] = failwith "never"

    [<JSEmit(Literals.getStringJS)>]
    member __.GetString(bytes : byte[]) : byte[] = failwith "never"

[<JS>]
type WebHeaderCollection() =
    let mutable headers = []

    member __.Add(key, value) = headers <- (key, value)::headers
    member __.Keys = headers |> List.map fst |> List.toArray
    member __.Values = headers |> List.map snd |> List.toArray

[<JS>]
type Stream(initalContents : byte[]) =
    let mutable contents = initalContents
    let mutable nextIndex = 0

    member __.Contents = contents

    member __.AsyncWrite(buffer:byte[], ?offset:int, ?count:int) =
        async {
            //TODO: Perf... could be more efficient!
            let offset = defaultArg offset 0
            let count  = defaultArg count buffer.Length
            let extra = Array.sub buffer offset count
            contents <- Array.append contents extra
        }
    member stream.AsyncRead(count) =
        async { 
            let start = nextIndex
            nextIndex <- nextIndex + count
            return Array.sub contents start count
        }
        
    member __.Dispose() = ()
    interface IDisposable with
        member __.Dispose() = ()

[<JS>]
type WebResponse(contents) =
    member __.GetResponseStream() = new Stream(contents)
    member __.ContentLength = int64 contents.Length

    member __.Dispose() = ()
    interface IDisposable with
        member __.Dispose() = ()

[<JS>]
type WebRequest(url : string) =
    let requestStream = new Stream([||])
    member val Headers = WebHeaderCollection()
    member val Method = "GET" with get, set
    member __.GetRequestStream() = requestStream
    member req.AsyncGetResponse() =
        Async.FromContinuations(fun (onSuccess, onError, _) ->
            let onReceived(data : string) =
                let bytes = Text.Encoding.UTF8.GetBytes data
                onSuccess(new WebResponse(bytes))
            let onErrorReceived() = onError null
            sendRequest(req.Method, url, req.Headers.Keys, req.Headers.Values, onReceived, onErrorReceived)
        )
        
    static member Create(uri : string) = WebRequest(uri)