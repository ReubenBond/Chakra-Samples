(function () {
  host.echo("Hello world!");

  host.doSuccessfulJsWork = function () {
    var workPromise = new Promise(function(resolve, reject) {
      resolve("Resolved promise from JavaScript");
    });

    return workPromise;
  };

  host.doUnsuccessfulJsWork = function () {
    var workPromise = new Promise(function (resolve, reject) {
      reject("Rejected promise from JavaScript");
    });

    return workPromise;
  };

  var successfulWork = host.doSuccessfulWork();
  var unsuccessfulWork = host.doUnsuccessfulWork();

  function resolveCallback(result) {
    host.echo('Resolved: ' + result);
  }

  function rejectCallback(reason) {
    host.echo('Rejected: ' + reason);
  }

  successfulWork.then(resolveCallback, rejectCallback);
  unsuccessfulWork.then(resolveCallback, rejectCallback);

  return 0;
})();