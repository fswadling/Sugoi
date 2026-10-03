module LruTests

// The residency ordering structure (spec 050): a move-to-head LRU over string keys. Pure, so
// its touch/tail/remove semantics are asserted with no device.

open Xunit
open Lru

[<Fact>]
let ``touch adds and moves a key to the head`` () =
    let lru = create ()
    touch lru "a"
    touch lru "b"
    touch lru "a"
    Assert.Equal<string list>([ "a"; "b" ], keys lru)

[<Fact>]
let ``tail is the least recently used`` () =
    let lru = create ()
    touch lru "a"
    touch lru "b"
    touch lru "c"
    Assert.Equal<string list>([ "c"; "b"; "a" ], keys lru)
    Assert.Equal(Some "a", tail lru)

[<Fact>]
let ``remove and contains track membership`` () =
    let lru = create ()
    touch lru "a"
    touch lru "b"
    Assert.True(contains lru "a")
    remove lru "a"
    Assert.False(contains lru "a")
    Assert.Equal(1, count lru)
    Assert.Equal(Some "b", tail lru)
    Assert.True(Option.isNone (tail (create ())))

[<Fact>]
let ``a removed key can be re-added at the head`` () =
    let lru = create ()
    touch lru "a"
    remove lru "a"
    touch lru "a"
    Assert.True(contains lru "a")
    Assert.Equal(1, count lru)
