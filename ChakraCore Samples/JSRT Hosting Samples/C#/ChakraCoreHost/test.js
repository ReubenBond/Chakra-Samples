(function() {
    host.echo("Hello world!");

    var jsPromiseFunc = async function(shouldThrow) {
        if (!shouldThrow) return "Resolved promise from JavaScript";
        throw "Rejected promise from JavaScript";
    };

    return (async function () {
        var result = await jsPromiseFunc(false);
        host.echo('Succcess: ' + result);
        try {
            await jsPromiseFunc(true);
        } catch (error) {
            host.echo('Error: ' + error);
        }

        result = await host.doSuccessfulWork();
        host.echo('Succcess: ' + result);
        try {
            await host.doUnsuccessfulWork();
        } catch (error) {
            host.echo('Error: ' + error);
        }
        
        host.echo('reading a web site: ' + await host.getUrl('https://httpbin.org/get'));

        host.echo('Finished!');

        return {
            sayHello: async (msg) => {
                var ip = await host.getUrl('https://httpbin.org/ip')
                host.echo('you asked me to say \"' + msg + '\" like some kind of talking monkey!');
                host.echo('btw, I hacked you and your ip is ' + ip);
            },
            add: (a, b) => {
                var result = a + b;
                host.echo('in JS land: ' + a + ' + ' + b + ' = ' + result);
                return result;
            }
        };
    })();
})();