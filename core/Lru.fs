module Lru

// A bounded-recently-used ordering over string keys: the head is the most recently used,
// the tail is the eviction candidate. A dictionary gives O(1) node lookup and a linked list
// O(1) move-to-head and tail read. It knows nothing about bytes — the residency budget is
// the caller's (`Assets`), which owns the size of what it caches.

open System.Collections.Generic

type Lru =
    { Order : LinkedList<string>
      Nodes : Dictionary<string, LinkedListNode<string>> }

let create () : Lru =
    { Order = LinkedList<string>()
      Nodes = Dictionary<string, LinkedListNode<string>>() }

// Whether the key is present, without touching its recency.
let contains (lru: Lru) (key: string) : bool =
    lru.Nodes.ContainsKey key

// Move the key to the head, adding it if absent.
let touch (lru: Lru) (key: string) : unit =
    match lru.Nodes.TryGetValue key with
    | true, node ->
        do lru.Order.Remove node
        do lru.Order.AddFirst node
    | false, _ ->
    let node = lru.Order.AddFirst key
    lru.Nodes.[key] <- node

let remove (lru: Lru) (key: string) : unit =
    match lru.Nodes.TryGetValue key with
    | false, _ -> ()
    | true, node ->
    do lru.Order.Remove node
    do lru.Nodes.Remove key |> ignore


// The least recently used key, if any.
let tail (lru: Lru) : string option =
    if lru.Order.Count = 0 then None else Some lru.Order.Last.Value

// Every key, most-recently-used first.
let keys (lru: Lru) : string list =
    lru.Order |> Seq.toList

let count (lru: Lru) : int =
    lru.Order.Count
