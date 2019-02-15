//split on thousands

function spaceSplitThousand(num) {    
    var newStr = [];
    var currentdigs = 0;
    while (num !== 0) {
        newStr.push((num % 10).toString());
        currentdigs++;
        if (currentdigs % 3 === 0)
            newStr.push(' ');
        num = Math.floor(num / 10);
    }
    newStr.reverse();
    return newStr.join('');
}